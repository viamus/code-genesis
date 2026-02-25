using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Pipeline;

namespace CodeGenesis.Engine.Steps;

public sealed class PlanStep(IClaudeRunner claude) : IPipelineStep
{
    public string Name => "Plan";
    public string Description => "Analyze requirements and create implementation plan";

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var response = await claude.RunAsync(new ClaudeRequest
        {
            Prompt = $"""
                You are a software architect. Analyze the following task and produce
                a detailed, numbered implementation plan. Be specific about which files
                to create or modify, and what changes to make in each.

                Task: {context.TaskDescription}
                """,
            SystemPrompt = "Respond with a clear, structured implementation plan. Number each step. Be specific about files and changes.",
            MaxTurns = 1,
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

        context.StepOutputs["plan"] = response.Result ?? "";
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
