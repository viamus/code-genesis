namespace CodeGenesis.Engine.Pipeline;

/// <summary>
/// Abstraction for executing a sequence of pipeline steps.
/// Used by composite steps (ForeachStep, ParallelStep) to recursively execute sub-steps.
/// </summary>
public interface IStepExecutor
{
    Task<bool> RunAsync(
        IReadOnlyList<IPipelineStep> steps,
        PipelineContext context,
        CancellationToken ct,
        Action<IPipelineStep>? onBeforeStep = null);
}
