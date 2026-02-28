using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;

namespace CodeGenesis.Engine.Steps;

public sealed class DynamicStep(
    IClaudeRunner claude,
    StepConfig stepConfig,
    string resolvedPrompt,
    string? resolvedSystemPrompt,
    string? model,
    Dictionary<string, McpServerConfig>? mcpServers = null,
    RetryPolicy? retryPolicy = null) : IPipelineStep
{
    private string _resolvedPrompt = resolvedPrompt;
    private string? _resolvedSystemPrompt = resolvedSystemPrompt;

    public string Name => stepConfig.Name;
    public string Description => stepConfig.Description ?? stepConfig.Name;
    public string OriginalPromptTemplate => stepConfig.Prompt;
    public string? OriginalSystemPromptTemplate => stepConfig.SystemPrompt;

    public void UpdateResolvedPrompt(string prompt) => _resolvedPrompt = prompt;
    public void UpdateResolvedSystemPrompt(string? systemPrompt) => _resolvedSystemPrompt = systemPrompt;

    /// <summary>Creates a shallow clone with its own mutable prompt state, safe for parallel execution.</summary>
    public DynamicStep Clone() => new(claude, stepConfig, _resolvedPrompt, _resolvedSystemPrompt, model, mcpServers, retryPolicy);

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var maxRetries = retryPolicy?.MaxRetries ?? 0;
        var backoffSeconds = retryPolicy?.BackoffSeconds ?? 10;
        var rateLimitPauseSeconds = retryPolicy?.RateLimitPauseSeconds ?? 60;
        var maxRateLimitPauses = retryPolicy?.MaxRateLimitPauses ?? 5;

        var attempt = 0;
        var rateLimitPauses = 0;
        var totalDuration = TimeSpan.Zero;
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var totalCost = 0.0;

        while (true)
        {
            var request = new ClaudeRequest
            {
                Prompt = _resolvedPrompt,
                SystemPrompt = _resolvedSystemPrompt,
                Model = model,
                MaxTurns = stepConfig.MaxTurns,
                WorkingDirectory = context.WorkingDirectory,
                AllowedTools = stepConfig.AllowedTools ?? [],
                McpServers = mcpServers,
                OnProgress = context.StatusUpdate is not null
                    ? evt =>
                    {
                        var icon = evt.Type == ClaudeProgressType.Thinking ? "\U0001F4AD" : "\U0001F527";
                        context.StatusUpdate?.Invoke($"{icon} {evt.Message}");
                    }
                    : null
            };

            var response = await claude.RunAsync(request, ct);

            // Accumulate metrics across attempts
            totalDuration += response.Duration;
            totalInputTokens += response.InputTokens;
            totalOutputTokens += response.OutputTokens;
            totalCost += response.CostUsd ?? 0;

            if (response.Success)
            {
                // Store output for downstream steps
                if (!string.IsNullOrWhiteSpace(stepConfig.OutputKey))
                    context.StepOutputs[stepConfig.OutputKey] = response.Result ?? "";

                context.TotalInputTokens += totalInputTokens;
                context.TotalOutputTokens += totalOutputTokens;
                context.TotalCostUsd += totalCost;

                // Check fail_if condition
                if (!string.IsNullOrWhiteSpace(stepConfig.FailIf)
                    && (response.Result ?? "").Contains(stepConfig.FailIf, StringComparison.OrdinalIgnoreCase))
                {
                    var failMessage = stepConfig.FailMessage
                        ?? $"Step '{stepConfig.Name}' output matched fail condition: {stepConfig.FailIf}";
                    return new StepResult
                    {
                        Outcome = StepOutcome.Failed,
                        Error = failMessage,
                        Duration = totalDuration,
                        TokensUsed = totalInputTokens + totalOutputTokens,
                        CostUsd = totalCost
                    };
                }

                return new StepResult
                {
                    Outcome = StepOutcome.Success,
                    Output = response.Result,
                    Duration = totalDuration,
                    TokensUsed = totalInputTokens + totalOutputTokens,
                    CostUsd = totalCost
                };
            }

            // --- Failure path: classify and decide whether to retry ---
            var failureKind = response.FailureKind;

            if (failureKind == ClaudeFailureKind.RateLimit)
            {
                rateLimitPauses++;
                if (rateLimitPauses > maxRateLimitPauses)
                    return FailResult(response.ErrorMessage, totalDuration, totalInputTokens + totalOutputTokens, totalCost);

                context.StatusUpdate?.Invoke(
                    $"\u23F8 Rate limited -- pausing {rateLimitPauseSeconds}s ({rateLimitPauses}/{maxRateLimitPauses})");
                await Task.Delay(TimeSpan.FromSeconds(rateLimitPauseSeconds), ct);
                continue; // Does NOT count as a retry attempt
            }

            if (failureKind == ClaudeFailureKind.Timeout)
            {
                attempt++;
                if (attempt > maxRetries)
                    return FailResult(response.ErrorMessage, totalDuration, totalInputTokens + totalOutputTokens, totalCost);

                var delay = backoffSeconds * attempt;
                context.StatusUpdate?.Invoke(
                    $"\u21BB Timed out -- retry {attempt}/{maxRetries} in {delay}s");
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                continue;
            }

            // Non-transient error (max_turns, parse error, etc.) — fail immediately
            return FailResult(response.ErrorMessage, totalDuration, totalInputTokens + totalOutputTokens, totalCost);
        }
    }

    private StepResult FailResult(string? error, TimeSpan duration, int tokensUsed, double cost)
    {
        if (stepConfig.Optional)
        {
            return new StepResult
            {
                Outcome = StepOutcome.Skipped,
                Error = error,
                Duration = duration,
                TokensUsed = tokensUsed,
                CostUsd = cost
            };
        }

        return new StepResult
        {
            Outcome = StepOutcome.Failed,
            Error = error,
            Duration = duration,
            TokensUsed = tokensUsed,
            CostUsd = cost
        };
    }
}
