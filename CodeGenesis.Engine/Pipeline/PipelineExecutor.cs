using System.Diagnostics;
using CodeGenesis.Engine.Steps;
using CodeGenesis.Engine.UI;
using Microsoft.Extensions.Logging;

namespace CodeGenesis.Engine.Pipeline;

public sealed class PipelineExecutor(
    PipelineRenderer renderer,
    ILogger<PipelineExecutor> logger) : IStepExecutor
{
    public async Task<bool> RunAsync(
        IReadOnlyList<IPipelineStep> steps,
        PipelineContext context,
        CancellationToken ct,
        Action<IPipelineStep>? onBeforeStep = null)
    {
        renderer.RenderPipelineStart(context, steps.Count);
        var pipelineSw = Stopwatch.StartNew();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            onBeforeStep?.Invoke(step);
            renderer.RenderStepStart(step, i + 1, steps.Count);

            StepResult result;
            try
            {
                // Foreach renders its own sequential progress interleaved with sub-steps;
                // wrapping it in a spinner would swallow that output.
                // Parallel/ParallelForeach suppress sub-step rendering, so the spinner
                // runs at the bottom while branch/item completions appear above it.
                if (step is ForeachStep)
                    result = await step.ExecuteAsync(context, ct);
                else
                    result = await renderer.RunWithSpinner(
                        step.Name,
                        () => step.ExecuteAsync(context, ct));
            }
            catch (OperationCanceledException)
            {
                renderer.RenderStepCancelled(step);
                context.StepsFailed++;
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Step '{Step}' threw unhandled exception", step.Name);
                renderer.RenderStepException(step, ex);
                context.StepsFailed++;
                return false;
            }

            renderer.RenderStepComplete(step, result);

            if (result.Outcome == StepOutcome.Failed)
            {
                logger.LogError("Pipeline aborted at step '{Step}': {Error}", step.Name, result.Error);
                context.StepsFailed++;
                pipelineSw.Stop();
                context.TotalDuration = pipelineSw.Elapsed;
                renderer.RenderPipelineFailed(context);
                return false;
            }

            context.StepsCompleted++;
        }

        pipelineSw.Stop();
        context.TotalDuration = pipelineSw.Elapsed;
        renderer.RenderPipelineSummary(context);
        return true;
    }
}
