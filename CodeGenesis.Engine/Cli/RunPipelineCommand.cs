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
    PipelineRenderer renderer,
    CheckpointManager checkpointManager) : AsyncCommand<RunPipelineCommandSettings>
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

        // Compute YAML hash for checkpoint comparison
        var yamlHash = checkpointManager.ComputeFileHash(settings.File);

        // Handle --resume / --from-step
        HashSet<string>? completedSteps = null;
        string? resumeFromStep = null;
        PipelineCheckpoint? checkpoint = null;

        if (settings.Resume || settings.FromStep is not null)
        {
            checkpoint = checkpointManager.Load(settings.File);

            if (checkpoint is null)
            {
                renderer.RenderError("No checkpoint found. Run the pipeline without --resume first.");
                return 1;
            }

            // Warn if YAML changed since checkpoint
            if (checkpoint.YamlHash != yamlHash)
            {
                renderer.RenderInfo("Warning: Pipeline YAML has changed since the last checkpoint. Resuming with current config.");
            }

            completedSteps = new HashSet<string>(checkpoint.CompletedSteps);

            if (settings.Resume)
            {
                // --resume: determine resume point from checkpoint
                resumeFromStep = checkpoint.FailedStepName;
                // If no failed step recorded, all completed steps will be skipped
                // and execution continues from the first non-completed step

                if (completedSteps.Count == 0 && resumeFromStep is null)
                {
                    renderer.RenderInfo("Nothing to resume — no steps were completed.");
                    return 0;
                }
            }
            else if (settings.FromStep is not null)
            {
                // --from-step: validate step exists in pipeline
                resumeFromStep = settings.FromStep;
            }

            // Restore cached step outputs into context
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

        // Restore cached step outputs from checkpoint
        if (checkpoint is not null && completedSteps is not null)
        {
            foreach (var (key, value) in checkpoint.StepOutputs)
            {
                if (completedSteps.Contains(key))
                    context.StepOutputs[key] = value;
            }
        }

        // Build step tree using StepBuilder
        List<IPipelineStep> steps;
        try
        {
            var builder = new StepBuilder(claude, executor, renderer, pipelineDir, globalModel, config.Settings, variables);
            steps = builder.BuildAll(config.Steps);
        }
        catch (Exception ex)
        {
            renderer.RenderError($"Failed to build pipeline steps: {ex.Message}");
            return 1;
        }

        // Validate --from-step target exists
        if (settings.FromStep is not null && !steps.Any(s => s.Name == settings.FromStep))
        {
            renderer.RenderError($"Step '{settings.FromStep}' not found in pipeline.");
            return 1;
        }

        // Check if all steps are already completed (--resume with nothing left)
        if (completedSteps is not null && resumeFromStep is null && steps.All(s => completedSteps.Contains(s.Name)))
        {
            renderer.RenderInfo("All steps already completed. Nothing to resume.");
            checkpointManager.Delete(settings.File);
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            renderer.RenderInfo("Cancelling pipeline...");
        };

        // Execute pipeline — resolve templates for each step just before it runs
        var success = await executor.RunAsync(steps, context, cts.Token,
            onBeforeStep: step =>
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
            },
            completedSteps: completedSteps,
            resumeFromStep: resumeFromStep,
            checkpointManager: checkpointManager,
            pipelineFile: settings.File,
            yamlHash: yamlHash,
            pipelineName: config.Pipeline.Name);

        if (!success)
        {
            renderer.RenderResumeHint(settings.File);
        }

        return success ? 0 : 1;
    }
}
