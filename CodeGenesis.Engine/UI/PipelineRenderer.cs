using CodeGenesis.Engine.Pipeline;
using Spectre.Console;

namespace CodeGenesis.Engine.UI;

public sealed class PipelineRenderer
{
    public void RenderBanner()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Subtle)));

        var banner = new Markup(
            $"  [{ConsoleTheme.PrimaryTag} bold]{ConsoleTheme.Spark} CodeGenesis Engine[/]  " +
            $"[{ConsoleTheme.MutedTag}]v0.1.0[/]");
        AnsiConsole.Write(banner);
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Subtle)));
        AnsiConsole.WriteLine();
    }

    public void RenderPipelineStart(PipelineContext context, int totalSteps)
    {
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.MutedTag}]Task[/]    {context.TaskDescription.EscapeMarkup()}");
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.MutedTag}]Steps[/]   {totalSteps}");

        if (context.WorkingDirectory is not null)
            AnsiConsole.MarkupLine(
                $"  [{ConsoleTheme.MutedTag}]Dir[/]     {context.WorkingDirectory.EscapeMarkup()}");

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Subtle)));
        AnsiConsole.WriteLine();
    }

    public void RenderStepStart(IPipelineStep step, int index, int total)
    {
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SecondaryTag} bold]{ConsoleTheme.Arrow} Step {index}/{total}[/]  " +
            $"[bold]{step.Name.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.MutedTag}]  {step.Description.EscapeMarkup()}[/]");
    }

    public async Task<StepResult> RunWithSpinner(string name, Func<Task<StepResult>> work)
    {
        StepResult result = null!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(new Style(ConsoleTheme.Primary))
            .StartAsync($"  [{ConsoleTheme.MutedTag}]Running {name.EscapeMarkup()}…[/]", async _ =>
            {
                result = await work();
            });
        return result;
    }

    public void RenderStepComplete(IPipelineStep step, StepResult result)
    {
        var (icon, colorTag) = result.Outcome switch
        {
            StepOutcome.Success => (ConsoleTheme.Check, ConsoleTheme.SuccessTag),
            StepOutcome.Failed => (ConsoleTheme.Cross, ConsoleTheme.ErrorTag),
            StepOutcome.Skipped => (ConsoleTheme.Dash, ConsoleTheme.MutedTag),
            _ => ("?", ConsoleTheme.MutedTag)
        };

        var duration = result.Duration.TotalSeconds switch
        {
            < 1 => $"{result.Duration.TotalMilliseconds:F0}ms",
            < 60 => $"{result.Duration.TotalSeconds:F1}s",
            _ => $"{result.Duration.TotalMinutes:F1}m"
        };

        AnsiConsole.MarkupLine(
            $"  [{colorTag}]{icon}[/] {step.Name.EscapeMarkup()}  " +
            $"[{ConsoleTheme.MutedTag}]{duration}[/]  " +
            $"[{ConsoleTheme.SubtleTag}]{result.TokensUsed:N0} tokens[/]");

        if (result.Outcome == StepOutcome.Failed && result.Error is not null)
        {
            AnsiConsole.MarkupLine(
                $"    [{ConsoleTheme.ErrorTag}]{result.Error.EscapeMarkup()}[/]");
        }

        AnsiConsole.WriteLine();
    }

    public void RenderStepCancelled(IPipelineStep step)
    {
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.WarningTag}]{ConsoleTheme.Cross}[/] {step.Name.EscapeMarkup()}  " +
            $"[{ConsoleTheme.WarningTag}]cancelled[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderStepException(IPipelineStep step, Exception ex)
    {
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.ErrorTag}]{ConsoleTheme.Cross}[/] {step.Name.EscapeMarkup()}  " +
            $"[{ConsoleTheme.ErrorTag}]exception[/]");
        AnsiConsole.MarkupLine(
            $"    [{ConsoleTheme.ErrorTag}]{ex.Message.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderPipelineSummary(PipelineContext context)
    {
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Subtle)));
        AnsiConsole.WriteLine();

        var totalTokens = context.TotalInputTokens + context.TotalOutputTokens;
        var duration = FormatDuration(context.TotalDuration);

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Key").PadRight(2))
            .AddColumn(new TableColumn("Value"));

        table.AddRow(
            new Markup($"  [{ConsoleTheme.SuccessTag} bold]{ConsoleTheme.Check} Pipeline Complete[/]"),
            new Markup(""));
        table.AddRow(
            new Markup($"  [{ConsoleTheme.MutedTag}]Duration[/]"),
            new Markup($"{duration}"));
        table.AddRow(
            new Markup($"  [{ConsoleTheme.MutedTag}]Steps[/]"),
            new Markup($"{context.StepsCompleted} completed"));
        table.AddRow(
            new Markup($"  [{ConsoleTheme.MutedTag}]Tokens[/]"),
            new Markup($"{totalTokens:N0} ({context.TotalInputTokens:N0} in / {context.TotalOutputTokens:N0} out)"));
        table.AddRow(
            new Markup($"  [{ConsoleTheme.MutedTag}]Cost[/]"),
            new Markup($"${context.TotalCostUsd:F4}"));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Subtle)));
        AnsiConsole.WriteLine();
    }

    public void RenderPipelineFailed(PipelineContext context)
    {
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Error)));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.ErrorTag} bold]{ConsoleTheme.Cross} Pipeline Failed[/]  " +
            $"[{ConsoleTheme.MutedTag}]{context.StepsCompleted} of {context.StepsCompleted + context.StepsFailed} steps completed[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Error)));
        AnsiConsole.WriteLine();
    }

    public void RenderForeachStart(string itemVar, int itemCount)
    {
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SecondaryTag}]foreach[/] [{ConsoleTheme.MutedTag}]{itemVar.EscapeMarkup()}[/]  " +
            $"[{ConsoleTheme.SubtleTag}]{itemCount} item(s)[/]");
    }

    public void RenderForeachIteration(string itemVar, string itemValue, int index, int total)
    {
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SecondaryTag}]  [{ConsoleTheme.MutedTag}][{index + 1}/{total}][/] " +
            $"{itemVar.EscapeMarkup()} = {itemValue.EscapeMarkup()}[/]");
    }

    public void RenderParallelStart(int branchCount, int? maxConcurrency)
    {
        var concurrencyInfo = maxConcurrency.HasValue
            ? $"max {maxConcurrency}"
            : "unlimited";
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SecondaryTag}]parallel[/] [{ConsoleTheme.MutedTag}]{branchCount} branch(es)[/]  " +
            $"[{ConsoleTheme.SubtleTag}]concurrency: {concurrencyInfo}[/]");
    }

    public void RenderParallelBranchComplete(string branchName, bool success)
    {
        var (icon, colorTag) = success
            ? (ConsoleTheme.Check, ConsoleTheme.SuccessTag)
            : (ConsoleTheme.Cross, ConsoleTheme.ErrorTag);
        AnsiConsole.MarkupLine(
            $"  [{colorTag}]  {icon} {branchName.EscapeMarkup()}[/]");
    }

    public void RenderError(string message)
    {
        AnsiConsole.MarkupLine($"  [{ConsoleTheme.ErrorTag}]{ConsoleTheme.Cross} {message.EscapeMarkup()}[/]");
    }

    public void RenderInfo(string message)
    {
        AnsiConsole.MarkupLine($"  [{ConsoleTheme.MutedTag}]{message.EscapeMarkup()}[/]");
    }

    private static string FormatDuration(TimeSpan ts) => ts.TotalSeconds switch
    {
        < 1 => $"{ts.TotalMilliseconds:F0}ms",
        < 60 => $"{ts.TotalSeconds:F1}s",
        < 3600 => $"{ts.Minutes}m {ts.Seconds}s",
        _ => $"{ts.Hours}h {ts.Minutes}m"
    };
}
