using System.Diagnostics;
using CodeGenesis.Engine.Config;
using CodeGenesis.Engine.Pipeline;

namespace CodeGenesis.Engine.Steps;

/// <summary>
/// Validates that a collection is non-empty and contains no null/whitespace items.
/// Place before a foreach step to fail early with a clear error message.
/// </summary>
public sealed class GuardStep(
    GuardConfig config,
    Func<string, Dictionary<string, string>, string> resolveTemplate,
    Dictionary<string, string> variables) : IPipelineStep
{
    public string Name => "guard";
    public string Description => config.ErrorMessage ?? "Guard: validate collection";

    public Task<StepResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Resolve the collection expression with current variables + step outputs
        var allVars = new Dictionary<string, string>(variables);
        foreach (var (key, value) in context.StepOutputs)
            allVars[$"steps.{key}"] = value;

        var collectionRaw = resolveTemplate(config.Collection, allVars);
        var items = CollectionParser.Parse(collectionRaw);

        var errorMessage = config.ErrorMessage ?? "Guard failed: collection is empty or contains invalid entries";

        if (items.Count == 0)
        {
            sw.Stop();
            return Task.FromResult(new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = errorMessage,
                Duration = sw.Elapsed
            });
        }

        if (items.Any(string.IsNullOrWhiteSpace))
        {
            sw.Stop();
            return Task.FromResult(new StepResult
            {
                Outcome = StepOutcome.Failed,
                Error = errorMessage,
                Duration = sw.Elapsed
            });
        }

        sw.Stop();
        return Task.FromResult(new StepResult
        {
            Outcome = StepOutcome.Success,
            Output = $"Guard passed: {items.Count} valid item(s)",
            Duration = sw.Elapsed
        });
    }
}
