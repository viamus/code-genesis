using CodeGenesis.Engine.Claude;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.Steps;
using CodeGenesis.Engine.UI;
using Microsoft.Extensions.Options;
using Spectre.Console.Cli;

namespace CodeGenesis.Engine.Cli;

public sealed class RunCommand(
    IClaudeRunner claude,
    IOptions<ClaudeCliOptions> claudeOptions,
    PipelineExecutor executor,
    PipelineRenderer renderer) : AsyncCommand<RunCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext commandContext, RunCommandSettings settings)
    {
        renderer.RenderBanner();

        // Apply settings overrides
        if (settings.Model is not null)
            claudeOptions.Value.DefaultModel = settings.Model;
        if (settings.MaxTurns > 0)
            claudeOptions.Value.MaxTurnsDefault = settings.MaxTurns;

        var context = new PipelineContext
        {
            TaskDescription = settings.Task,
            WorkingDirectory = settings.Directory ?? Directory.GetCurrentDirectory()
        };

        // Assemble the pipeline
        var steps = new List<IPipelineStep>
        {
            new PlanStep(claude),
            new ExecuteStep(claude, claudeOptions)
        };

        if (!settings.SkipValidate)
            steps.Add(new ValidateStep(claude));

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            renderer.RenderInfo("Cancelling pipeline...");
        };

        var success = await executor.RunAsync(steps, context, cts.Token);
        return success ? 0 : 1;
    }
}
