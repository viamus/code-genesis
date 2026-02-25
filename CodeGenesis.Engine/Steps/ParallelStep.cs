using System.Diagnostics;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Composite step that runs branches concurrently using Task.WhenAll with optional concurrency limiting.
/// </summary>
public sealed class ParallelStep(
    ParallelConfig config,
    IReadOnlyList<(ParallelBranch Branch, IReadOnlyList<IPipelineStep> Steps)> branches,
    IStepExecutor executor,
    PipelineRenderer renderer,
    Func<string, Dictionary<string, string>, string> resolveTemplate,
    Dictionary<string, string> variables) : IPipelineStep
{
    public string Name => $"parallel ({branches.Count} branches)";
    public string Description => $"Run {branches.Count} branch(es) concurrently";

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        renderer.RenderParallelStart(branches.Count, config.MaxConcurrency);

        var maxConcurrency = config.MaxConcurrency ?? int.MaxValue;
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var branchResults = new (bool Success, PipelineContext Context, string BranchName)[branches.Count];
        var tasks = new Task[branches.Count];

        for (var i = 0; i < branches.Count; i++)
        {
            var index = i;
            var (branch, branchSteps) = branches[i];

            tasks[i] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(linkedCts.Token);
                try
                {
                    // Create isolated context for this branch
                    var branchContext = new PipelineContext
                    {
                        TaskDescription = branch.Name,
                        WorkingDirectory = context.WorkingDirectory
                    };

                    // Copy parent step outputs for reads
                    foreach (var (key, value) in context.StepOutputs)
                        branchContext.StepOutputs[key] = value;

                    // Resolve sub-step templates with current variables
                    var branchVars = new Dictionary<string, string>(variables);
                    foreach (var (key, value) in context.StepOutputs)
                        branchVars[$"steps.{key}"] = value;

                    var success = await executor.RunAsync(branchSteps, branchContext, linkedCts.Token,
                        onBeforeStep: step =>
                        {
                            if (step is DynamicStep dynamicStep)
                            {
                                var allVars = new Dictionary<string, string>(branchVars);
                                foreach (var (key, value) in branchContext.StepOutputs)
                                    allVars[$"steps.{key}"] = value;

                                dynamicStep.UpdateResolvedPrompt(
                                    resolveTemplate(dynamicStep.OriginalPromptTemplate, allVars));

                                if (dynamicStep.OriginalSystemPromptTemplate is not null)
                                    dynamicStep.UpdateResolvedSystemPrompt(
                                        resolveTemplate(dynamicStep.OriginalSystemPromptTemplate, allVars));
                            }
                        });

                    branchResults[index] = (success, branchContext, branch.Name);

                    if (!success && config.FailFast)
                        await linkedCts.CancelAsync();

                    renderer.RenderParallelBranchComplete(branch.Name, success);
                }
                finally
                {
                    semaphore.Release();
                }
            }, linkedCts.Token);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (config.FailFast)
        {
            // Expected when fail_fast cancels siblings
        }

        sw.Stop();

        // Merge results back into parent context
        var allSuccess = true;
        var totalTokens = 0;
        var totalCost = 0.0;

        foreach (var (success, branchCtx, branchName) in branchResults)
        {
            if (branchCtx is null) continue; // Branch didn't run (cancelled before start)

            // Merge branch outputs into parent
            var branchConfig = branches.First(b => b.Branch.Name == branchName).Branch;
            if (branchConfig.OutputKey is not null)
            {
                // Collect all outputs from this branch under its output_key
                var branchOutputs = new Dictionary<string, string>(branchCtx.StepOutputs);
                foreach (var key in context.StepOutputs.Keys)
                    branchOutputs.Remove(key);

                // If there's exactly one output, store it directly; otherwise store as JSON
                if (branchOutputs.Count == 1)
                    context.StepOutputs[branchConfig.OutputKey] = branchOutputs.Values.First();
                else if (branchOutputs.Count > 0)
                    context.StepOutputs[branchConfig.OutputKey] =
                        System.Text.Json.JsonSerializer.Serialize(branchOutputs);
            }
            else
            {
                // No output_key: merge individual outputs directly
                foreach (var (key, value) in branchCtx.StepOutputs)
                {
                    if (!context.StepOutputs.ContainsKey(key))
                        context.StepOutputs[key] = value;
                }
            }

            context.TotalInputTokens += branchCtx.TotalInputTokens;
            context.TotalOutputTokens += branchCtx.TotalOutputTokens;
            context.TotalCostUsd += branchCtx.TotalCostUsd;
            context.StepsCompleted += branchCtx.StepsCompleted;
            totalTokens += branchCtx.TotalInputTokens + branchCtx.TotalOutputTokens;
            totalCost += branchCtx.TotalCostUsd;

            if (!success)
            {
                allSuccess = false;
                context.StepsFailed += branchCtx.StepsFailed;
            }
        }

        return new StepResult
        {
            Outcome = allSuccess ? StepOutcome.Success : StepOutcome.Failed,
            Output = allSuccess
                ? $"All {branches.Count} branches completed"
                : "One or more branches failed",
            Error = allSuccess ? null : "Parallel execution had failures",
            Duration = sw.Elapsed,
            TokensUsed = totalTokens,
            CostUsd = totalCost
        };
    }
}
