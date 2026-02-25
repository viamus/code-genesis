using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Pipeline;

namespace CodeGenesis.Engine.Steps;

public sealed class ValidateStep(IClaudeRunner claude) : IPipelineStep
{
    public string Name => "Validate";
    public string Description => "Review the implementation for correctness and quality";

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var plan = context.StepOutputs.GetValueOrDefault("plan", "(no plan)");
        var executeResult = context.StepOutputs.GetValueOrDefault("execute_result", "(no execution result)");

        var response = await claude.RunAsync(new ClaudeRequest
        {
            Prompt = $"""
                Review the following implementation against the original plan and task.
                Check for correctness, completeness, code quality, and potential issues.

                ## Original Task
                {context.TaskDescription}

                ## Plan
                {plan}

                ## Implementation Result
                {executeResult}

                Provide a validation report:
                1. Were all plan steps implemented?
                2. Are there any bugs or issues?
                3. Code quality assessment
                4. Final verdict: PASS or FAIL with reasoning
                """,
            SystemPrompt = "You are a code reviewer. Be thorough but fair. Focus on correctness and completeness.",
            MaxTurns = 3,
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

        context.StepOutputs["validation"] = response.Result ?? "";
        context.TotalInputTokens += response.InputTokens;
        context.TotalOutputTokens += response.OutputTokens;
        context.TotalCostUsd += response.CostUsd ?? 0;

        // Check if validation passed by looking for FAIL verdict
        var output = response.Result ?? "";
        var failed = output.Contains("FAIL", StringComparison.OrdinalIgnoreCase)
                     && !output.Contains("NOT FAIL", StringComparison.OrdinalIgnoreCase);

        return new StepResult
        {
            Outcome = failed ? StepOutcome.Failed : StepOutcome.Success,
            Output = response.Result,
            Error = failed ? "Validation did not pass. See output for details." : null,
            Duration = response.Duration,
            TokensUsed = response.InputTokens + response.OutputTokens,
            CostUsd = response.CostUsd ?? 0
        };
    }
}
