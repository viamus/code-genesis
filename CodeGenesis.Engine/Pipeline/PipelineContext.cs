namespace CodeGenesis.Engine.Pipeline;

public sealed class PipelineContext
{
    public required string TaskDescription { get; init; }
    public string? WorkingDirectory { get; init; }

    /// <summary>Each step writes its output here; subsequent steps read from prior steps.</summary>
    public Dictionary<string, string> StepOutputs { get; } = new();

    /// <summary>Accumulated metrics across all steps.</summary>
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public double TotalCostUsd { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public int StepsCompleted { get; set; }
    public int StepsFailed { get; set; }
}
