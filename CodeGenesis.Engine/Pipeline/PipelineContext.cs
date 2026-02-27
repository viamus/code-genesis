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

    /// <summary>Set when the pipeline aborts — holds the step name and error for the failure banner.</summary>
    public string? FailedStepName { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>Callback to update the visual status of the currently running step (spinner text or thinking line).</summary>
    public Action<string>? StatusUpdate { get; set; }
}
