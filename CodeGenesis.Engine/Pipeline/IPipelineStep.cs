namespace CodeGenesis.Engine.Pipeline;

public interface IPipelineStep
{
    string Name { get; }
    string Description { get; }
    Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct);
}
