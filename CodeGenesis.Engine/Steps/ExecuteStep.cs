using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Pipeline;
using Microsoft.Extensions.Options;

namespace CodeGenesis.Engine.Steps;

public sealed class ExecuteStep(
    IClaudeRunner claude,
    IOptions<ClaudeCliOptions> options) : IPipelineStep
{
    public string Name => "Execute";
    public string Description => "Implement the plan using Claude's agentic tools";

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        if (!context.StepOutputs.TryGetValue("plan", out var plan))
        {
            return new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = "No plan found in context. Run the Plan step first."
            };
        }

        var response = await claude.RunAsync(new ClaudeRequest
        {
            Prompt = $"""
                You have the following implementation plan. Execute it step by step.
                Use the available tools to read, write, and modify files as needed.

                ## Plan
                {plan}

                ## Original Task
                {context.TaskDescription}

                Implement the plan now. Be thorough and precise.
                """,
            SystemPrompt = "You are a senior software engineer. Implement the given plan precisely. Use tools to read/write files. Do not skip steps.",
            MaxTurns = options.Value.MaxTurnsDefault == 0 ? null : options.Value.MaxTurnsDefault,
            WorkingDirectory = context.WorkingDirectory
        }, ct);

        if (!response.Success)
        {
            return new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = response.ErrorMessage,
                Duration = response.Duration
            };
        }

        context.StepOutputs["execute_result"] = response.Result ?? "";
        context.TotalInputTokens += response.InputTokens;
        context.TotalOutputTokens += response.OutputTokens;
        context.TotalCostUsd += response.CostUsd ?? 0;

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
