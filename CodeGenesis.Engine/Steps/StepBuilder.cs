using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Recursively builds IPipelineStep trees from StepEntry YAML models.
/// </summary>
public sealed class StepBuilder(
    IClaudeRunner claude,
    IStepExecutor executor,
    PipelineRenderer renderer,
    string pipelineDir,
    string? globalModel,
    PipelineSettings? globalSettings,
    Dictionary<string, string> variables)
{
    public List<IPipelineStep> BuildAll(IReadOnlyList<StepEntry> entries)
    {
        return entries.Select(Build).ToList();
    }

    public IPipelineStep Build(StepEntry entry)
    {
        if (entry.IsForeach)
            return BuildForeach(entry.Foreach!);

        if (entry.IsParallel)
            return BuildParallel(entry.Parallel!);

        if (entry.IsParallelForeach)
            return BuildParallelForeach(entry.ParallelForeach!);

        if (entry.IsApproval)
            return new ApprovalStep(entry.Approval!, renderer);

        return BuildSimple(entry);
    }

    private IPipelineStep BuildSimple(StepEntry entry)
    {
        var stepConfig = entry.ToStepConfig();

        // Load context bundle if specified
        AgentDefinition? bundle = null;
        if (!string.IsNullOrWhiteSpace(stepConfig.Context))
        {
            bundle = ContextBundleLoader.LoadBundle(stepConfig.Context, pipelineDir);
            ApplyBundle(stepConfig, bundle);
        }

        var stepModel = stepConfig.Model ?? bundle?.Model ?? globalModel;

        var resolvedPrompt = PipelineConfigLoader.ResolveTemplate(stepConfig.Prompt, variables);
        var resolvedSystemPrompt = stepConfig.SystemPrompt is not null
            ? PipelineConfigLoader.ResolveTemplate(stepConfig.SystemPrompt, variables)
            : null;

        var retryPolicy = RetryPolicy.Resolve(stepConfig, globalSettings);
        return new DynamicStep(claude, stepConfig, resolvedPrompt, resolvedSystemPrompt, stepModel, retryPolicy);
    }

    private ForeachStep BuildForeach(ForeachConfig config)
    {
        var subSteps = BuildAll(config.Steps);
        return new ForeachStep(
            config, subSteps, executor, renderer,
            PipelineConfigLoader.ResolveTemplate, variables);
    }

    private ParallelForeachStep BuildParallelForeach(ParallelForeachConfig config)
    {
        var subSteps = BuildAll(config.Steps);
        return new ParallelForeachStep(
            config, subSteps, executor, renderer,
            PipelineConfigLoader.ResolveTemplate, variables);
    }

    private ParallelStep BuildParallel(ParallelConfig config)
    {
        var branches = config.Branches.Select(branch =>
        {
            var branchSteps = BuildAll(branch.Steps);
            return (branch, (IReadOnlyList<IPipelineStep>)branchSteps);
        }).ToList();

        return new ParallelStep(
            config, branches, executor, renderer,
            PipelineConfigLoader.ResolveTemplate, variables);
    }

    private static void ApplyBundle(StepConfig stepConfig, AgentDefinition bundle)
    {
        if (bundle.SystemPrompt is not null)
            stepConfig.SystemPrompt = bundle.SystemPrompt;
        if (bundle.Prompt is not null)
            stepConfig.Prompt = bundle.Prompt;
        if (bundle.MaxTurns is not null)
            stepConfig.MaxTurns = bundle.MaxTurns;
        if (bundle.AllowedTools is not null)
            stepConfig.AllowedTools = bundle.AllowedTools;
    }
}
