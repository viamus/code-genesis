using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.Steps;
using CodeGenesis.Engine.UI;
using Spectre.Console.Cli;

namespace CodeGenesis.Engine.Cli;

public sealed class RunPipelineCommand(
    IClaudeRunner claude,
    PipelineExecutor executor,
    PipelineRenderer renderer) : AsyncCommand<RunPipelineCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext commandContext, RunPipelineCommandSettings settings)
    {
        renderer.RenderBanner();

        // Load YAML config
        PipelineConfig config;
        try
        {
            config = PipelineConfigLoader.LoadFromFile(settings.File);
        }
        catch (Exception ex)
        {
            renderer.RenderError($"Failed to load pipeline: {ex.Message}");
            return 1;
        }

        // Build template variables from inputs (defaults + overrides)
        var variables = new Dictionary<string, string>();
        foreach (var (key, input) in config.Inputs)
        {
            if (input.Default is not null)
                variables[key] = input.Default;
        }

        // Apply CLI input overrides
        if (settings.Input is not null)
        {
            foreach (var kvp in settings.Input)
            {
                var eqIndex = kvp.IndexOf('=');
                if (eqIndex <= 0)
                {
                    renderer.RenderError($"Invalid input format '{kvp}'. Expected key=value.");
                    return 1;
                }

                var key = kvp[..eqIndex];
                var value = kvp[(eqIndex + 1)..];
                variables[key] = value;
            }
        }

        // Determine global model: CLI override > YAML settings > null (use default)
        var globalModel = settings.Model ?? config.Settings.Model;

        // Determine working directory
        var workingDir = settings.Directory
            ?? config.Settings.WorkingDirectory
            ?? Directory.GetCurrentDirectory();

        // Pipeline file directory for resolving relative context paths
        var pipelineDir = Path.GetDirectoryName(Path.GetFullPath(settings.File)) ?? Directory.GetCurrentDirectory();

        var context = new PipelineContext
        {
            TaskDescription = config.Pipeline.Name,
            WorkingDirectory = workingDir
        };

        // Build dynamic steps
        var steps = new List<IPipelineStep>();
        foreach (var stepConfig in config.Steps)
        {
            // Load context bundle if specified
            AgentDefinition? bundle = null;
            if (!string.IsNullOrWhiteSpace(stepConfig.Context))
            {
                try
                {
                    bundle = ContextBundleLoader.LoadBundle(stepConfig.Context, pipelineDir);
                    ApplyBundle(stepConfig, bundle);
                }
                catch (Exception ex)
                {
                    renderer.RenderError($"Failed to load context bundle for step '{stepConfig.Name}': {ex.Message}");
                    return 1;
                }
            }

            // Resolve model: step.Model > bundle.Model > globalModel
            var stepModel = stepConfig.Model ?? bundle?.Model ?? globalModel;

            // Resolve template placeholders in the prompt
            var resolvedPrompt = PipelineConfigLoader.ResolveTemplate(stepConfig.Prompt, variables);
            var resolvedSystemPrompt = stepConfig.SystemPrompt is not null
                ? PipelineConfigLoader.ResolveTemplate(stepConfig.SystemPrompt, variables)
                : null;

            var step = new DynamicStep(claude, stepConfig, resolvedPrompt, resolvedSystemPrompt, stepModel);
            steps.Add(step);
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            renderer.RenderInfo("Cancelling pipeline...");
        };

        // Execute pipeline — resolve templates for each step just before it runs
        var success = await executor.RunAsync(steps, context, cts.Token, onBeforeStep: step =>
        {
            if (step is DynamicStep dynamicStep)
            {
                // Re-resolve the prompt and system prompt with latest step outputs
                var allVars = new Dictionary<string, string>(variables);
                foreach (var (key, value) in context.StepOutputs)
                    allVars[$"steps.{key}"] = value;

                dynamicStep.UpdateResolvedPrompt(
                    PipelineConfigLoader.ResolveTemplate(
                        dynamicStep.OriginalPromptTemplate, allVars));

                if (dynamicStep.OriginalSystemPromptTemplate is not null)
                {
                    dynamicStep.UpdateResolvedSystemPrompt(
                        PipelineConfigLoader.ResolveTemplate(
                            dynamicStep.OriginalSystemPromptTemplate, allVars));
                }
            }
        });

        return success ? 0 : 1;
    }

    private static void ApplyBundle(StepConfig stepConfig, AgentDefinition bundle)
    {
        // Bundle overrides inline values when defined
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
