using System.Diagnostics;
using System.Text.Json;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Composite step that iterates over a collection and runs sub-steps for each item.
/// </summary>
public sealed class ForeachStep(
    ForeachConfig config,
    IReadOnlyList<IPipelineStep> subSteps,
    IStepExecutor executor,
    PipelineRenderer renderer,
    Func<string, Dictionary<string, string>, string> resolveTemplate,
    Dictionary<string, string> variables) : IPipelineStep
{
    public string Name => $"foreach ({config.ItemVar} in collection)";
    public string Description => $"Iterate over collection, {subSteps.Count} sub-step(s) per item";

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
                Error = "Foreach collection is empty — stopping pipeline",
                Duration = sw.Elapsed
            };
        }

        var iterationResults = new List<Dictionary<string, string>>();
        var totalTokens = 0;
        var totalCost = 0.0;

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = items[i];
            renderer.RenderForeachIteration(config.ItemVar, item, i, items.Count);

            // Create a scoped context for this iteration
            var iterationContext = new PipelineContext
            {
                TaskDescription = context.TaskDescription,
                WorkingDirectory = context.WorkingDirectory
            };

            // Copy parent step outputs so sub-steps can read them
            foreach (var (key, value) in context.StepOutputs)
                iterationContext.StepOutputs[key] = value;

            // Inject loop variables into template variables for sub-step resolution
            var iterVars = new Dictionary<string, string>(allVars)
            {
                ["loop.item"] = item,
                ["loop.index"] = i.ToString(),
                [config.ItemVar] = item
            };

            // Re-resolve sub-step prompts with loop variables and execute
            var resolvedSubSteps = ResolveSubStepTemplates(subSteps, iterVars);

            var success = await executor.RunAsync(resolvedSubSteps, iterationContext, ct,
                onBeforeStep: step => ResolveBeforeStep(step, iterVars, iterationContext));

            // Collect iteration outputs
            var iterOutputs = new Dictionary<string, string>(iterationContext.StepOutputs);
            // Remove parent outputs to get only this iteration's outputs
            foreach (var key in context.StepOutputs.Keys)
                iterOutputs.Remove(key);
            iterationResults.Add(iterOutputs);

            // Accumulate metrics
            totalTokens += iterationContext.TotalInputTokens + iterationContext.TotalOutputTokens;
            totalCost += iterationContext.TotalCostUsd;
            context.TotalInputTokens += iterationContext.TotalInputTokens;
            context.TotalOutputTokens += iterationContext.TotalOutputTokens;
            context.TotalCostUsd += iterationContext.TotalCostUsd;

            if (!success)
            {
                sw.Stop();
                // Store partial results
                StoreAggregatedResults(context, iterationResults);
                return new StepResult
                {
                    Outcome = StepOutcome.Failed,
                    Error = $"Foreach iteration {i} ({item}) failed",
                    Duration = sw.Elapsed,
                    TokensUsed = totalTokens,
                    CostUsd = totalCost
                };
            }

            context.StepsCompleted += iterationContext.StepsCompleted;
        }

        sw.Stop();

        // Store aggregated results
        StoreAggregatedResults(context, iterationResults);

        return new StepResult
        {
            Outcome = StepOutcome.Success,
            Output = $"Completed {items.Count} iteration(s)",
            Duration = sw.Elapsed,
            TokensUsed = totalTokens,
            CostUsd = totalCost
        };
    }

    private void StoreAggregatedResults(PipelineContext context, List<Dictionary<string, string>> iterationResults)
    {
        if (config.OutputKey is not null)
        {
            var json = JsonSerializer.Serialize(iterationResults);
            context.StepOutputs[config.OutputKey] = json;
        }
    }

    private static IReadOnlyList<IPipelineStep> ResolveSubStepTemplates(
        IReadOnlyList<IPipelineStep> steps, Dictionary<string, string> vars)
    {
        // For DynamicSteps, update the resolved prompt with loop variables
        foreach (var step in steps)
        {
            if (step is DynamicStep dynamicStep)
            {
                dynamicStep.UpdateResolvedPrompt(
                    ResolveAllVars(dynamicStep.OriginalPromptTemplate, vars));

                if (dynamicStep.OriginalSystemPromptTemplate is not null)
                    dynamicStep.UpdateResolvedSystemPrompt(
                        ResolveAllVars(dynamicStep.OriginalSystemPromptTemplate, vars));
            }
        }
        return steps;
    }

    private static void ResolveBeforeStep(IPipelineStep step, Dictionary<string, string> iterVars, PipelineContext ctx)
    {
        if (step is DynamicStep dynamicStep)
        {
            var allVars = new Dictionary<string, string>(iterVars);
            foreach (var (key, value) in ctx.StepOutputs)
                allVars[$"steps.{key}"] = value;

            dynamicStep.UpdateResolvedPrompt(
                ResolveAllVars(dynamicStep.OriginalPromptTemplate, allVars));

            if (dynamicStep.OriginalSystemPromptTemplate is not null)
                dynamicStep.UpdateResolvedSystemPrompt(
                    ResolveAllVars(dynamicStep.OriginalSystemPromptTemplate, allVars));
        }
    }

    private static string ResolveAllVars(string template, Dictionary<string, string> vars)
    {
        return PipelineConfigLoader.ResolveTemplate(template, vars);
    }
}
