using System.Diagnostics;
using CodeGenesis.Engine.Steps;
using CodeGenesis.Engine.UI;
using Microsoft.Extensions.Logging;

namespace CodeGenesis.Engine.Pipeline;

public sealed class PipelineExecutor(
    PipelineRenderer renderer,
    ILogger<PipelineExecutor> logger) : IStepExecutor
{
    /// <summary>
    /// IStepExecutor implementation — used by composite steps (foreach, parallel) internally.
    /// No resume logic; executes all steps unconditionally.
    /// </summary>
    async Task<bool> IStepExecutor.RunAsync(
        IReadOnlyList<IPipelineStep> steps,
        PipelineContext context,
        CancellationToken ct,
        Action<IPipelineStep>? onBeforeStep)
    {
        return await RunCoreAsync(steps, context, ct, onBeforeStep,
            completedSteps: null, resumeFromStep: null,
            checkpointManager: null, pipelineFile: null, yamlHash: null, pipelineName: null);
    }

    /// <summary>
    /// Public entry point called by RunPipelineCommand — supports resume parameters.
    /// </summary>
    public async Task<bool> RunAsync(
        IReadOnlyList<IPipelineStep> steps,
        PipelineContext context,
        CancellationToken ct,
        Action<IPipelineStep>? onBeforeStep = null,
        HashSet<string>? completedSteps = null,
        string? resumeFromStep = null,
        CheckpointManager? checkpointManager = null,
        string? pipelineFile = null,
        string? yamlHash = null,
        string? pipelineName = null)
    {
        return await RunCoreAsync(steps, context, ct, onBeforeStep,
            completedSteps, resumeFromStep,
            checkpointManager, pipelineFile, yamlHash, pipelineName);
    }

    private async Task<bool> RunCoreAsync(
        IReadOnlyList<IPipelineStep> steps,
        PipelineContext context,
        CancellationToken ct,
        Action<IPipelineStep>? onBeforeStep,
        HashSet<string>? completedSteps,
        string? resumeFromStep,
        CheckpointManager? checkpointManager,
        string? pipelineFile,
        string? yamlHash,
        string? pipelineName)
    {
        var isResuming = completedSteps is not null;
        var doneSkipping = !isResuming;

        renderer.RenderPipelineStart(context, steps.Count);
        var pipelineSw = Stopwatch.StartNew();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            // Resume logic: skip completed steps until we reach the resume point
            if (!doneSkipping)
            {
                if (resumeFromStep is not null && step.Name == resumeFromStep)
                {
                    // Found the explicit --from-step target: stop skipping, execute this step
                    doneSkipping = true;
                }
                else if (resumeFromStep is null && !completedSteps!.Contains(step.Name))
                {
                    // --resume mode: stop skipping at the first non-completed step
                    doneSkipping = true;
                }
                else
                {
                    // This step was completed before — skip it
                    renderer.RenderStepSkipped(step, i + 1, steps.Count);
                    context.StepsCompleted++;
                    continue;
                }
            }

            onBeforeStep?.Invoke(step);
            renderer.RenderStepStart(step, i + 1, steps.Count);

            StepResult result;
            try
            {
                if (step is ForeachStep or ParallelStep or ParallelForeachStep or ApprovalStep)
                    result = await step.ExecuteAsync(context, ct);
                else
                    result = await renderer.RunWithSpinner(
                        step.Name,
                        context,
                        () => step.ExecuteAsync(context, ct));
            }
            catch (OperationCanceledException)
            {
                renderer.RenderStepCancelled(step);
                context.StepsFailed++;
                context.FailedStepName = step.Name;
                context.FailureReason = "Pipeline was cancelled.";
                pipelineSw.Stop();
                context.TotalDuration = pipelineSw.Elapsed;
                SaveCheckpointOnFailure(checkpointManager, pipelineFile, yamlHash, pipelineName, context, steps, i, step.Name);
                renderer.RenderPipelineFailed(context);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Step '{Step}' threw unhandled exception", step.Name);
                renderer.RenderStepException(step, ex);
                context.StepsFailed++;
                context.FailedStepName = step.Name;
                context.FailureReason = ex.Message;
                pipelineSw.Stop();
                context.TotalDuration = pipelineSw.Elapsed;
                SaveCheckpointOnFailure(checkpointManager, pipelineFile, yamlHash, pipelineName, context, steps, i, step.Name);
                renderer.RenderPipelineFailed(context);
                return false;
            }

            renderer.RenderStepComplete(step, result);

            if (result.Outcome == StepOutcome.Failed)
            {
                logger.LogError("Pipeline aborted at step '{Step}': {Error}", step.Name, result.Error);
                context.StepsFailed++;
                context.FailedStepName = step.Name;
                context.FailureReason = result.Error;
                pipelineSw.Stop();
                context.TotalDuration = pipelineSw.Elapsed;
                SaveCheckpointOnFailure(checkpointManager, pipelineFile, yamlHash, pipelineName, context, steps, i, step.Name);
                renderer.RenderPipelineFailed(context);
                return false;
            }

            context.StepsCompleted++;

            // Save checkpoint after each successful step
            SaveCheckpointAfterStep(checkpointManager, pipelineFile, yamlHash, pipelineName, context, steps, i);
        }

        pipelineSw.Stop();
        context.TotalDuration = pipelineSw.Elapsed;
        renderer.RenderPipelineSummary(context);

        // Pipeline completed successfully — delete checkpoint
        if (checkpointManager is not null && pipelineFile is not null)
            checkpointManager.Delete(pipelineFile);

        return true;
    }

    private static void SaveCheckpointAfterStep(
        CheckpointManager? checkpointManager,
        string? pipelineFile,
        string? yamlHash,
        string? pipelineName,
        PipelineContext context,
        IReadOnlyList<IPipelineStep> steps,
        int currentIndex)
    {
        if (checkpointManager is null || pipelineFile is null || yamlHash is null || pipelineName is null)
            return;

        // All steps up to and including currentIndex have completed
        var completed = steps.Take(currentIndex + 1).Select(s => s.Name).ToList();

        var checkpoint = new PipelineCheckpoint
        {
            PipelineFile = Path.GetFileName(pipelineFile),
            PipelineName = pipelineName,
            YamlHash = yamlHash,
            LastUpdatedUtc = DateTime.UtcNow,
            CompletedSteps = completed,
            StepOutputs = new Dictionary<string, string>(context.StepOutputs),
            FailedStepName = null,
            Metrics = new CheckpointMetrics
            {
                TotalInputTokens = context.TotalInputTokens,
                TotalOutputTokens = context.TotalOutputTokens,
                TotalCostUsd = context.TotalCostUsd,
                StepsCompleted = context.StepsCompleted
            }
        };

        checkpointManager.Save(checkpoint, pipelineFile);
    }

    private static void SaveCheckpointOnFailure(
        CheckpointManager? checkpointManager,
        string? pipelineFile,
        string? yamlHash,
        string? pipelineName,
        PipelineContext context,
        IReadOnlyList<IPipelineStep> steps,
        int failedIndex,
        string failedStepName)
    {
        if (checkpointManager is null || pipelineFile is null || yamlHash is null || pipelineName is null)
            return;

        var completed = new List<string>();
        for (var j = 0; j < failedIndex; j++)
        {
            if (context.StepOutputs.ContainsKey(steps[j].Name))
                completed.Add(steps[j].Name);
        }

        var checkpoint = new PipelineCheckpoint
        {
            PipelineFile = Path.GetFileName(pipelineFile),
            PipelineName = pipelineName,
            YamlHash = yamlHash,
            LastUpdatedUtc = DateTime.UtcNow,
            CompletedSteps = completed,
            StepOutputs = new Dictionary<string, string>(context.StepOutputs),
            FailedStepName = failedStepName,
            Metrics = new CheckpointMetrics
            {
                TotalInputTokens = context.TotalInputTokens,
                TotalOutputTokens = context.TotalOutputTokens,
                TotalCostUsd = context.TotalCostUsd,
                StepsCompleted = context.StepsCompleted
            }
        };

        checkpointManager.Save(checkpoint, pipelineFile);
    }
}
