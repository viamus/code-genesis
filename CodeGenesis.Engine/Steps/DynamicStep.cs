using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;

namespace CodeGenesis.Engine.Steps;

public sealed class DynamicStep(
    IClaudeRunner claude,
    StepConfig stepConfig,
    string resolvedPrompt,
    string? resolvedSystemPrompt,
    string? model) : IPipelineStep
{
    private string _resolvedPrompt = resolvedPrompt;
    private string? _resolvedSystemPrompt = resolvedSystemPrompt;

    public string Name => stepConfig.Name;
    public string Description => stepConfig.Description ?? stepConfig.Name;
    public string OriginalPromptTemplate => stepConfig.Prompt;
    public string? OriginalSystemPromptTemplate => stepConfig.SystemPrompt;

    public void UpdateResolvedPrompt(string prompt) => _resolvedPrompt = prompt;
    public void UpdateResolvedSystemPrompt(string? systemPrompt) => _resolvedSystemPrompt = systemPrompt;

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var request = new ClaudeRequest
        {
            Prompt = _resolvedPrompt,
            SystemPrompt = _resolvedSystemPrompt,
            Model = model,
            MaxTurns = stepConfig.MaxTurns,
            WorkingDirectory = context.WorkingDirectory,
            AllowedTools = stepConfig.AllowedTools ?? []
        };

        var response = await claude.RunAsync(request, ct);

        if (!response.Success)
        {
            if (stepConfig.Optional)
            {
                return new StepResult
                {
                    Outcome = StepOutcome.Skipped,
                    Error = response.ErrorMessage,
                    Duration = response.Duration
                };
            }

            return new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = response.ErrorMessage,
                Duration = response.Duration
            };
        }

        // Store output for downstream steps
        if (!string.IsNullOrWhiteSpace(stepConfig.OutputKey))
            context.StepOutputs[stepConfig.OutputKey] = response.Result ?? "";

        context.TotalInputTokens += response.InputTokens;
        context.TotalOutputTokens += response.OutputTokens;
        context.TotalCostUsd += response.CostUsd ?? 0;

        // Check fail_if condition: if the LLM output contains the trigger, stop the pipeline
        if (!string.IsNullOrWhiteSpace(stepConfig.FailIf)
            && (response.Result ?? "").Contains(stepConfig.FailIf, StringComparison.OrdinalIgnoreCase))
        {
            var failMessage = stepConfig.FailMessage
                ?? $"Step '{stepConfig.Name}' output matched fail condition: {stepConfig.FailIf}";
            return new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = failMessage,
                Duration = response.Duration,
                TokensUsed = response.InputTokens + response.OutputTokens,
                CostUsd = response.CostUsd ?? 0
            };
        }

        return new StepResult
        {
            Outcome = StepOutcome.Success,
            Output = response.Result,
            Duration = response.Duration,
            TokensUsed = response.InputTokens + response.OutputTokens,
            CostUsd = response.CostUsd ?? 0
        };
    }
}
