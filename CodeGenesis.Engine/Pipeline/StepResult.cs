namespace CodeGenesis.Engine.Pipeline;

public enum StepOutcome
{
    Success,
    Failed,
    Skipped
}

public sealed record StepResult
{
    public required StepOutcome Outcome { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
    public int TokensUsed { get; init; }
    public double CostUsd { get; init; }
}
