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

        if (entry.IsUsePipeline)
            return BuildUsePipeline(entry);

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

        // Merge MCP servers: global → bundle → step (later wins on key collision)
        var mergedMcpServers = MergeMcpServers(
            globalSettings?.McpServers,
            bundle?.McpServers,
            stepConfig.McpServers);

        // Resolve templates in MCP server args/env values
        if (mergedMcpServers is not null)
        {
            mergedMcpServers = mergedMcpServers.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ResolveTemplates(
                    val => PipelineConfigLoader.ResolveTemplate(val, variables)));
        }

        var resolvedPrompt = PipelineConfigLoader.ResolveTemplate(stepConfig.Prompt, variables);
        var resolvedSystemPrompt = stepConfig.SystemPrompt is not null
            ? PipelineConfigLoader.ResolveTemplate(stepConfig.SystemPrompt, variables)
            : null;

        // Append MCP tool documentation to system prompt when available
        if (mergedMcpServers is { Count: > 0 })
        {
            var mcpSection = McpContextBuilder.Build(mergedMcpServers);
            if (mcpSection is not null)
            {
                resolvedSystemPrompt = resolvedSystemPrompt is not null
                    ? resolvedSystemPrompt + "\n\n" + mcpSection
                    : mcpSection;
            }
        }

        var retryPolicy = RetryPolicy.Resolve(stepConfig, globalSettings);
        return new DynamicStep(claude, stepConfig, resolvedPrompt, resolvedSystemPrompt, stepModel, mergedMcpServers, retryPolicy);
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

    private UsePipelineStep BuildUsePipeline(StepEntry entry)
    {
        return new UsePipelineStep(
            name: entry.Name ?? "sub-pipeline",
            pipelinePath: entry.UsePipeline!,
            pipelineDir: pipelineDir,
            inputs: entry.Inputs,
            outputKey: entry.OutputKey,
            optional: entry.Optional,
            claude: claude,
            executor: executor,
            renderer: renderer,
            parentVariables: variables);
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

    /// <summary>
    /// Merges MCP server dictionaries with later sources overriding earlier ones by key.
    /// Returns null if all sources are null/empty.
    /// </summary>
    private static Dictionary<string, McpServerConfig>? MergeMcpServers(
        params Dictionary<string, McpServerConfig>?[] sources)
    {
        Dictionary<string, McpServerConfig>? merged = null;

        foreach (var source in sources)
        {
            if (source is null || source.Count == 0)
                continue;

            merged ??= new Dictionary<string, McpServerConfig>();
            foreach (var (key, value) in source)
                merged[key] = value;
        }

        return merged;
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
