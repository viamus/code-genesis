using System.Diagnostics;
using System.Text.Json;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Composite step that iterates over a collection and runs sub-steps for each item concurrently.
/// Combines ForeachStep's collection iteration with ParallelStep's concurrency model.
/// </summary>
public sealed class ParallelForeachStep(
    ParallelForeachConfig config,
    IReadOnlyList<IPipelineStep> subSteps,
    IStepExecutor executor,
    PipelineRenderer renderer,
    Func<string, Dictionary<string, string>, string> resolveTemplate,
    Dictionary<string, string> variables) : IPipelineStep
{
    public string Name => $"parallel_foreach ({config.ItemVar} in collection)";
    public string Description => $"Iterate over collection concurrently, {subSteps.Count} sub-step(s) per item";

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Resolve the collection expression with current variables + step outputs
        var allVars = new Dictionary<string, string>(variables);
        foreach (var (key, value) in context.StepOutputs)
            allVars[$"steps.{key}"] = value;

        var collectionRaw = resolveTemplate(config.Collection, allVars);
        var items = CollectionParser.Parse(collectionRaw);

        renderer.RenderForeachStart(config.ItemVar, items.Count);

        if (items.Count == 0)
        {
            sw.Stop();
            return new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = "Parallel foreach collection is empty — stopping pipeline",
                Duration = sw.Elapsed
            };
        }

        var maxConcurrency = config.MaxConcurrency ?? int.MaxValue;
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var iterationResults = new (bool Success, PipelineContext Context, string Item, int Index)[items.Count];
        var tasks = new Task[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var item = items[i];

            tasks[i] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(linkedCts.Token);
                try
                {
                    // Skip null or empty items
                    if (string.IsNullOrWhiteSpace(item))
                    {
                        renderer.RenderForeachIteration(config.ItemVar, "(empty)", index, items.Count);
                        var emptyCtx = new PipelineContext
                        {
                            TaskDescription = context.TaskDescription,
                            WorkingDirectory = context.WorkingDirectory
                        };
                        iterationResults[index] = (true, emptyCtx, item, index);
                        return;
                    }

                    renderer.RenderForeachIteration(config.ItemVar, item, index, items.Count);

                    // Create isolated context for this iteration
                    var iterationContext = new PipelineContext
                    {
                        TaskDescription = context.TaskDescription,
                        WorkingDirectory = context.WorkingDirectory
                    };

                    // Copy parent step outputs so sub-steps can read them
                    foreach (var (key, value) in context.StepOutputs)
                        iterationContext.StepOutputs[key] = value;

                    // Build loop variables for this iteration
                    var iterVars = new Dictionary<string, string>(allVars)
                    {
                        ["loop.item"] = item,
                        ["loop.index"] = index.ToString(),
                        [config.ItemVar] = item
                    };

                    // Clone sub-steps for this iteration so parallel threads don't share mutable state
                    var clonedSubSteps = CloneSubSteps(subSteps, iterVars);

                    var iterSw = Stopwatch.StartNew();

                    var success = await executor.RunAsync(clonedSubSteps, iterationContext, linkedCts.Token,
                        onBeforeStep: step => ResolveBeforeStep(step, iterVars, iterationContext));

                    iterSw.Stop();

                    iterationResults[index] = (success, iterationContext, item, index);

                    if (!success && config.FailFast)
                        await linkedCts.CancelAsync();

                    var iterTokens = iterationContext.TotalInputTokens + iterationContext.TotalOutputTokens;
                    renderer.RenderForeachIterationComplete(item, index, items.Count, iterSw.Elapsed, iterTokens, iterationContext.TotalCostUsd);
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

        // Aggregate results
        var allSuccess = true;
        var totalTokens = 0;
        var totalCost = 0.0;
        var aggregatedResults = new List<Dictionary<string, string>>();

        foreach (var (success, iterCtx, item, index) in iterationResults)
        {
            if (iterCtx is null)
            {
                aggregatedResults.Add(new Dictionary<string, string>());
                continue;
            }

            // Collect iteration-specific outputs (exclude parent outputs)
            var iterOutputs = new Dictionary<string, string>(iterCtx.StepOutputs);
            foreach (var key in context.StepOutputs.Keys)
                iterOutputs.Remove(key);
            aggregatedResults.Add(iterOutputs);

            // Accumulate metrics into parent context
            context.TotalInputTokens += iterCtx.TotalInputTokens;
            context.TotalOutputTokens += iterCtx.TotalOutputTokens;
            context.TotalCostUsd += iterCtx.TotalCostUsd;
            context.StepsCompleted += iterCtx.StepsCompleted;
            totalTokens += iterCtx.TotalInputTokens + iterCtx.TotalOutputTokens;
            totalCost += iterCtx.TotalCostUsd;

            if (!success)
            {
                allSuccess = false;
                context.StepsFailed += iterCtx.StepsFailed;
            }
        }

        // Store aggregated results under output_key
        if (config.OutputKey is not null)
        {
            var json = JsonSerializer.Serialize(aggregatedResults);
            context.StepOutputs[config.OutputKey] = json;
        }

        return new StepResult
        {
            Outcome = allSuccess ? StepOutcome.Success : StepOutcome.Failed,
            Output = allSuccess
                ? $"Completed {items.Count} iteration(s) concurrently"
                : "One or more iterations failed",
            Error = allSuccess ? null : "Parallel foreach execution had failures",
            Duration = sw.Elapsed,
            TokensUsed = totalTokens,
            CostUsd = totalCost
        };
    }

    private static IReadOnlyList<IPipelineStep> CloneSubSteps(
        IReadOnlyList<IPipelineStep> steps, Dictionary<string, string> vars)
    {
        var cloned = new List<IPipelineStep>(steps.Count);
        foreach (var step in steps)
        {
            if (step is DynamicStep dynamicStep)
            {
                var clone = dynamicStep.Clone();
                clone.UpdateResolvedPrompt(
                    PipelineConfigLoader.ResolveTemplate(clone.OriginalPromptTemplate, vars));

                if (clone.OriginalSystemPromptTemplate is not null)
                    clone.UpdateResolvedSystemPrompt(
                        PipelineConfigLoader.ResolveTemplate(clone.OriginalSystemPromptTemplate, vars));

                cloned.Add(clone);
            }
            else
            {
                cloned.Add(step);
            }
        }
        return cloned;
    }

    private static void ResolveBeforeStep(IPipelineStep step, Dictionary<string, string> iterVars, PipelineContext ctx)
    {
        if (step is DynamicStep dynamicStep)
        {
            var allVars = new Dictionary<string, string>(iterVars);
            foreach (var (key, value) in ctx.StepOutputs)
                allVars[$"steps.{key}"] = value;

            dynamicStep.UpdateResolvedPrompt(
                PipelineConfigLoader.ResolveTemplate(dynamicStep.OriginalPromptTemplate, allVars));

            if (dynamicStep.OriginalSystemPromptTemplate is not null)
                dynamicStep.UpdateResolvedSystemPrompt(
                    PipelineConfigLoader.ResolveTemplate(dynamicStep.OriginalSystemPromptTemplate, allVars));
        }
    }
}
