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

        // Build step tree using StepBuilder
        List<IPipelineStep> steps;
        try
        {
            var builder = new StepBuilder(claude, executor, renderer, pipelineDir, globalModel, variables);
            steps = builder.BuildAll(config.Steps);
        }
        catch (Exception ex)
        {
            renderer.RenderError($"Failed to build pipeline steps: {ex.Message}");
            return 1;
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
}
