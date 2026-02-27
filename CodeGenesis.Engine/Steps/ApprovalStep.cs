using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;
using CodeGenesis.Engine.UI;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Pauses pipeline execution and prompts the user to approve or reject before continuing.
/// Renders a styled panel in the console with an optional preview of a previous step's output.
/// </summary>
public sealed class ApprovalStep(ApprovalConfig config, PipelineRenderer renderer) : IPipelineStep
{
    public string Name => config.Name;
    public string Description => config.Description ?? config.Message;

    public Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Resolve the optional display value from a previous step output
        string? displayValue = null;
        if (!string.IsNullOrWhiteSpace(config.DisplayKey)
            && context.StepOutputs.TryGetValue(config.DisplayKey, out var val))
        {
            displayValue = val;
        }

        bool approved = renderer.RenderApprovalPrompt(config.Message, displayValue, ct);

        sw.Stop();

        var outcome = approved ? StepOutcome.Success : StepOutcome.Failed;
        var error = approved ? null : "Pipeline rejected by user at approval step.";

        return Task.FromResult(new StepResult
        {
            Outcome = outcome,
            Error = error,
            Duration = sw.Elapsed
        });
    }
}
