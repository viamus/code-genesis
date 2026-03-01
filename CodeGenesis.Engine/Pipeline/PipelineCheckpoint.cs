namespace CodeGenesis.Engine.Pipeline;

public sealed class PipelineCheckpoint
{
    public int Version { get; init; } = 1;
    public required string PipelineFile { get; init; }
    public required string PipelineName { get; init; }
    public required string YamlHash { get; init; }
    public required DateTime LastUpdatedUtc { get; init; }
    public required List<string> CompletedSteps { get; init; }
    public required Dictionary<string, string> StepOutputs { get; init; }
    public string? FailedStepName { get; init; }
    public required CheckpointMetrics Metrics { get; init; }
}

public sealed class CheckpointMetrics
{
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
    public double TotalCostUsd { get; init; }
    public int StepsCompleted { get; init; }
}
