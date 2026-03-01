using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Composite step that loads and executes another pipeline YAML as a sub-pipeline.
/// Supports input mapping from parent context and output merging back into parent.
/// </summary>
public sealed class UsePipelineStep(
    string name,
    string pipelinePath,
    string pipelineDir,
    Dictionary<string, string>? inputs,
    string? outputKey,
    bool optional,
    IClaudeRunner claude,
    IStepExecutor executor,
    PipelineRenderer renderer,
    Dictionary<string, string> parentVariables) : IPipelineStep
{
    /// <summary>
    /// Tracks canonical paths of all pipelines in the current async call chain for circular reference detection.
    /// Each parallel branch gets an isolated copy via AsyncLocal semantics.
    /// </summary>
    private static readonly AsyncLocal<ImmutableHashSet<string>> ActivePipelines = new();

    public string Name => name;
    public string Description => $"Sub-pipeline: {pipelinePath}";

    public async Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Resolve pipeline path relative to parent pipeline directory
            var resolvedPath = Path.IsPathRooted(pipelinePath)
                ? pipelinePath
                : Path.GetFullPath(Path.Combine(pipelineDir, pipelinePath));

            // Circular reference detection
            var canonicalPath = Path.GetFullPath(resolvedPath);
            var activePipelines = ActivePipelines.Value ?? ImmutableHashSet<string>.Empty;

            if (activePipelines.Contains(canonicalPath))
            {
                return FailOrSkip(
                    $"Circular pipeline reference detected: {pipelinePath} is already in the call chain",
                    sw.Elapsed, 0, 0);
            }

            // Push current pipeline onto the active set
            ActivePipelines.Value = activePipelines.Add(canonicalPath);

            try
            {
                // Validate file exists
                if (!File.Exists(resolvedPath))
                {
                    return FailOrSkip(
                        $"Sub-pipeline file not found: {resolvedPath}",
                        sw.Elapsed, 0, 0);
                }

                // Load child pipeline config
                var childConfig = PipelineConfigLoader.LoadFromFile(resolvedPath);
                var childPipelineDir = Path.GetDirectoryName(resolvedPath) ?? pipelineDir;

                // Build child variables: start with child's input defaults, override with parent's inputs
                var childVariables = BuildChildVariables(childConfig, context);

                // Build child steps
                var childGlobalModel = string.IsNullOrWhiteSpace(childConfig.Settings.Model)
                    ? null : childConfig.Settings.Model;
                var childBuilder = new StepBuilder(
                    claude, executor, renderer,
                    childPipelineDir, childGlobalModel, childConfig.Settings, childVariables);
                var childSteps = childBuilder.BuildAll(childConfig.Steps);

                renderer.RenderSubPipelineStart(name, pipelinePath, childSteps.Count);
                renderer.PushScope();

                // Create isolated context for child execution
                var childContext = new PipelineContext
                {
                    TaskDescription = childConfig.Pipeline.Name,
                    WorkingDirectory = childConfig.Settings.WorkingDirectory ?? context.WorkingDirectory
                };

                // Execute child pipeline
                var success = await executor.RunAsync(childSteps, childContext, ct,
                    onBeforeStep: step => ResolveBeforeStep(step, childVariables, childContext));

                renderer.PopScope();

                // Accumulate metrics into parent
                var totalTokens = childContext.TotalInputTokens + childContext.TotalOutputTokens;
                var totalCost = childContext.TotalCostUsd;
                context.TotalInputTokens += childContext.TotalInputTokens;
                context.TotalOutputTokens += childContext.TotalOutputTokens;
                context.TotalCostUsd += childContext.TotalCostUsd;
                context.StepsCompleted += childContext.StepsCompleted;

                sw.Stop();

                if (!success)
                {
                    context.StepsFailed += childContext.StepsFailed;
                    renderer.RenderSubPipelineComplete(name, false, childSteps.Count, sw.Elapsed, totalTokens, totalCost);

                    return FailOrSkip(
                        $"Sub-pipeline '{childConfig.Pipeline.Name}' failed" +
                        (childContext.FailureReason is not null ? $": {childContext.FailureReason}" : ""),
                        sw.Elapsed, totalTokens, totalCost);
                }

                // Merge outputs back into parent context
                MergeOutputs(childConfig, childContext, context);

                renderer.RenderSubPipelineComplete(name, true, childSteps.Count, sw.Elapsed, totalTokens, totalCost);

                return new StepResult
                {
                    Outcome = StepOutcome.Success,
                    Output = $"Sub-pipeline '{childConfig.Pipeline.Name}' completed ({childSteps.Count} steps)",
                    Duration = sw.Elapsed,
                    TokensUsed = totalTokens,
                    CostUsd = totalCost
                };
            }
            finally
            {
                // Pop current pipeline from the active set
                ActivePipelines.Value = activePipelines;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return FailOrSkip(ex.Message, sw.Elapsed, 0, 0);
        }
    }

    private Dictionary<string, string> BuildChildVariables(PipelineConfig childConfig, PipelineContext context)
    {
        var childVars = new Dictionary<string, string>();

        // Start with child pipeline's input defaults
        foreach (var (key, input) in childConfig.Inputs)
        {
            if (input.Default is not null)
                childVars[key] = input.Default;
        }

        // Override with parent's inputs mappings (resolved with parent context)
        if (inputs is not null)
        {
            var parentVars = new Dictionary<string, string>(parentVariables);
            foreach (var (key, value) in context.StepOutputs)
                parentVars[$"steps.{key}"] = value;

            foreach (var (key, template) in inputs)
            {
                childVars[key] = PipelineConfigLoader.ResolveTemplate(template, parentVars);
            }
        }

        return childVars;
    }

    private void MergeOutputs(PipelineConfig childConfig, PipelineContext childContext, PipelineContext parentContext)
    {
        // Determine which outputs to expose
        Dictionary<string, string> childOutputs;

        if (childConfig.Outputs.Count > 0)
        {
            // Child declares explicit outputs — only expose those
            childOutputs = new Dictionary<string, string>();
            foreach (var (outputName, outputDef) in childConfig.Outputs)
            {
                if (outputDef.Source is not null && childContext.StepOutputs.TryGetValue(outputDef.Source, out var value))
                    childOutputs[outputName] = value;
            }
        }
        else
        {
            // No explicit outputs — expose all child step outputs
            childOutputs = new Dictionary<string, string>(childContext.StepOutputs);
        }

        if (outputKey is not null)
        {
            // Store under parent's output_key
            if (childOutputs.Count == 1)
                parentContext.StepOutputs[outputKey] = childOutputs.Values.First();
            else if (childOutputs.Count > 1)
                parentContext.StepOutputs[outputKey] = JsonSerializer.Serialize(childOutputs);
            else
                parentContext.StepOutputs[outputKey] = "";
        }
        else
        {
            // Merge directly into parent (no overwrites)
            foreach (var (key, value) in childOutputs)
            {
                if (!parentContext.StepOutputs.ContainsKey(key))
                    parentContext.StepOutputs[key] = value;
            }
        }
    }

    private static void ResolveBeforeStep(IPipelineStep step, Dictionary<string, string> vars, PipelineContext ctx)
    {
        if (step is DynamicStep dynamicStep)
        {
            var allVars = new Dictionary<string, string>(vars);
            foreach (var (key, value) in ctx.StepOutputs)
                allVars[$"steps.{key}"] = value;

            dynamicStep.UpdateResolvedPrompt(
                PipelineConfigLoader.ResolveTemplate(dynamicStep.OriginalPromptTemplate, allVars));

            if (dynamicStep.OriginalSystemPromptTemplate is not null)
                dynamicStep.UpdateResolvedSystemPrompt(
                    PipelineConfigLoader.ResolveTemplate(dynamicStep.OriginalSystemPromptTemplate, allVars));
        }
    }

    private StepResult FailOrSkip(string error, TimeSpan duration, int tokensUsed, double cost)
    {
        return new StepResult
        {
            Outcome = optional ? StepOutcome.Skipped : StepOutcome.Failed,
            Error = error,
            Duration = duration,
            TokensUsed = tokensUsed,
            CostUsd = cost
        };
    }
}
