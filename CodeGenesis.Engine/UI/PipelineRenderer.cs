using CodeGenesis.Engine.Pipeline;
using Spectre.Console;

namespace CodeGenesis.Engine.UI;

public sealed class PipelineRenderer
{
    // Shared spinner guard — only one Spectre.Console Status can run at a time
    private int _spinnerActive;

    // AsyncLocal so each parallel task gets its own rendering state
    private readonly AsyncLocal<int> _depth = new();
    private readonly AsyncLocal<bool> _renderingSuppressed = new();

    // ── Depth / scope management ──────────────────────────────────────

    public void PushScope() => _depth.Value++;
    public void PopScope() => _depth.Value = Math.Max(0, _depth.Value - 1);

    /// <summary>Suppresses all step-level rendering (step start/complete/spinner) in the current async flow.</summary>
    public void SuppressRendering() => _renderingSuppressed.Value = true;
    public void ResumeRendering() => _renderingSuppressed.Value = false;

    private bool IsSuppressed => _renderingSuppressed.Value;

    private string Indent => _depth.Value > 0
        ? new string(' ', 2) + string.Concat(Enumerable.Repeat($"[{ConsoleTheme.SubtleTag}]│[/]  ", _depth.Value))
        : "  ";

    // ── Banner ────────────────────────────────────────────────────────

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

    // ── Pipeline lifecycle ────────────────────────────────────────────

    public void RenderPipelineStart(PipelineContext context, int totalSteps)
    {
        // Suppress nested pipeline headers (e.g. inside foreach/parallel iterations)
        if (_depth.Value > 0 || IsSuppressed) return;

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

    public void RenderPipelineSummary(PipelineContext context)
    {
        // Suppress nested pipeline summaries
        if (_depth.Value > 0 || IsSuppressed) return;

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
        // Suppress nested pipeline failure banners
        if (_depth.Value > 0 || IsSuppressed) return;

        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Error)));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.ErrorTag} bold]{ConsoleTheme.Cross} Pipeline Failed[/]  " +
            $"[{ConsoleTheme.MutedTag}]{context.StepsCompleted} of {context.StepsCompleted + context.StepsFailed} steps completed[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(ConsoleTheme.Error)));
        AnsiConsole.WriteLine();
    }

    // ── Step lifecycle ────────────────────────────────────────────────

    public void RenderStepStart(IPipelineStep step, int index, int total)
    {
        if (IsSuppressed) return;
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.SecondaryTag} bold]{ConsoleTheme.Arrow} Step {index}/{total}[/]  " +
            $"[bold]{step.Name.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.MutedTag}]  {step.Description.EscapeMarkup()}[/]");
    }

    public async Task<StepResult> RunWithSpinner(string name, Func<Task<StepResult>> work)
    {
        // Skip spinner when suppressed or another spinner is already active
        // (Spectre.Console Status is a singleton — only one can run at a time)
        if (IsSuppressed || Interlocked.CompareExchange(ref _spinnerActive, 1, 0) != 0)
            return await work();

        StepResult result = null!;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots2)
                .SpinnerStyle(new Style(ConsoleTheme.Primary))
                .StartAsync($"{Indent}[{ConsoleTheme.MutedTag}]Running {name.EscapeMarkup()}…[/]", async _ =>
                {
                    result = await work();
                });
        }
        finally
        {
            Interlocked.Exchange(ref _spinnerActive, 0);
        }
        return result;
    }

    public void RenderStepComplete(IPipelineStep step, StepResult result)
    {
        if (IsSuppressed) return;
        var (icon, colorTag) = result.Outcome switch
        {
            StepOutcome.Success => (ConsoleTheme.Check, ConsoleTheme.SuccessTag),
            StepOutcome.Failed => (ConsoleTheme.Cross, ConsoleTheme.ErrorTag),
            StepOutcome.Skipped => (ConsoleTheme.Dash, ConsoleTheme.MutedTag),
            _ => ("?", ConsoleTheme.MutedTag)
        };

        var metrics = FormatMetrics(result.Duration, result.TokensUsed, result.CostUsd);

        AnsiConsole.MarkupLine(
            $"{Indent}[{colorTag}]{icon}[/] {step.Name.EscapeMarkup()}  {metrics}");

        if (result.Outcome == StepOutcome.Failed && result.Error is not null)
        {
            AnsiConsole.MarkupLine(
                $"{Indent}  [{ConsoleTheme.ErrorTag}]{result.Error.EscapeMarkup()}[/]");
        }

        AnsiConsole.WriteLine();
    }

    public void RenderStepCancelled(IPipelineStep step)
    {
        if (IsSuppressed) return;
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.WarningTag}]{ConsoleTheme.Cross}[/] {step.Name.EscapeMarkup()}  " +
            $"[{ConsoleTheme.WarningTag}]cancelled[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderStepException(IPipelineStep step, Exception ex)
    {
        if (IsSuppressed) return;
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.ErrorTag}]{ConsoleTheme.Cross}[/] {step.Name.EscapeMarkup()}  " +
            $"[{ConsoleTheme.ErrorTag}]exception[/]");
        AnsiConsole.MarkupLine(
            $"{Indent}  [{ConsoleTheme.ErrorTag}]{ex.Message.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();
    }

    // ── Foreach ───────────────────────────────────────────────────────

    public void RenderForeachStart(string itemVar, int itemCount)
    {
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.SecondaryTag}]foreach[/] " +
            $"[{ConsoleTheme.MutedTag}]{itemVar.EscapeMarkup()}[/]  " +
            $"[{ConsoleTheme.SubtleTag}]{itemCount} item(s)[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderForeachIteration(string itemVar, string itemValue, int index, int total)
    {
        var isLast = index == total - 1;
        var connector = isLast ? "\u2514" : "\u250c"; // └ or ┌

        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SubtleTag}]{connector}[/] " +
            $"[{ConsoleTheme.SecondaryTag} bold][[{index + 1}/{total}]][/] " +
            $"[bold]{Truncate(itemValue, 50).EscapeMarkup()}[/]");
    }

    public void RenderForeachIterationComplete(string itemValue, int index, int total, TimeSpan elapsed, int tokens, double cost)
    {
        var isLast = index == total - 1;
        var connector = isLast ? " " : "\u2514"; // (space) or └
        var metrics = FormatMetrics(elapsed, tokens, cost);

        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SubtleTag}]{connector}[/] " +
            $"[{ConsoleTheme.SuccessTag}]{ConsoleTheme.Check}[/] " +
            $"[{ConsoleTheme.MutedTag}]done[/]  {metrics}");
        AnsiConsole.WriteLine();
    }

    // ── Parallel Foreach ──────────────────────────────────────────────

    public void RenderParallelForeachStart(string itemVar, int itemCount, int? maxConcurrency)
    {
        var concurrencyInfo = maxConcurrency.HasValue
            ? $"max {maxConcurrency}"
            : "unlimited";
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.PrimaryTag}]\u26a1[/] " +
            $"[{ConsoleTheme.SecondaryTag}]parallel_foreach[/] " +
            $"[{ConsoleTheme.MutedTag}]{itemVar.EscapeMarkup()}[/]  " +
            $"[{ConsoleTheme.SubtleTag}]{itemCount} item(s)  concurrency: {concurrencyInfo}[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderParallelForeachItemStart(string itemValue, int index, int total)
    {
        AnsiConsole.MarkupLine(
            $"{Indent}  [{ConsoleTheme.PrimaryTag}]\u25cb[/] " +
            $"[{ConsoleTheme.SecondaryTag}][[{index + 1}/{total}]][/] " +
            $"[bold]{Truncate(itemValue, 50).EscapeMarkup()}[/]");
    }

    public void RenderParallelForeachItemComplete(string itemValue, int index, int total, bool success, TimeSpan elapsed, int tokens, double cost)
    {
        var (icon, colorTag) = success
            ? (ConsoleTheme.Check, ConsoleTheme.SuccessTag)
            : (ConsoleTheme.Cross, ConsoleTheme.ErrorTag);
        var metrics = FormatMetrics(elapsed, tokens, cost);

        AnsiConsole.MarkupLine(
            $"{Indent}  [{colorTag}]{icon}[/] " +
            $"[{ConsoleTheme.MutedTag}][[{index + 1}/{total}]][/] " +
            $"{Truncate(itemValue, 50).EscapeMarkup()}  {metrics}");
    }

    public void RenderParallelForeachEnd(int total, int succeeded, int failed)
    {
        AnsiConsole.WriteLine();
        if (failed == 0)
        {
            AnsiConsole.MarkupLine(
                $"{Indent}  [{ConsoleTheme.SuccessTag}]\u26a1 {total}/{total} completed[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"{Indent}  [{ConsoleTheme.ErrorTag}]\u26a1 {succeeded} completed, {failed} failed[/]");
        }
    }

    // ── Parallel ──────────────────────────────────────────────────────

    public void RenderParallelStart(int branchCount, int? maxConcurrency)
    {
        var concurrencyInfo = maxConcurrency.HasValue
            ? $"max {maxConcurrency}"
            : "unlimited";
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.SecondaryTag}]parallel[/] " +
            $"[{ConsoleTheme.MutedTag}]{branchCount} branch(es)[/]  " +
            $"[{ConsoleTheme.SubtleTag}]concurrency: {concurrencyInfo}[/]");
    }

    public void RenderParallelBranchComplete(string branchName, bool success, TimeSpan elapsed, int tokens, double cost)
    {
        var (icon, colorTag) = success
            ? (ConsoleTheme.Check, ConsoleTheme.SuccessTag)
            : (ConsoleTheme.Cross, ConsoleTheme.ErrorTag);
        var metrics = FormatMetrics(elapsed, tokens, cost);
        AnsiConsole.MarkupLine(
            $"{Indent}  [{colorTag}]{icon}[/] {branchName.EscapeMarkup()}  {metrics}");
    }

    // ── Approval ──────────────────────────────────────────────────────

    /// <summary>
    /// Renders a styled approval panel and prompts the user for confirmation.
    /// Returns true if the user approves, false if they reject.
    /// </summary>
    public bool RenderApprovalPrompt(string message, string? displayValue, CancellationToken ct)
    {
        AnsiConsole.WriteLine();

        // Build panel content
        var content = new Markup(
            $"[{ConsoleTheme.WarningTag} bold]  {"\u26A0"} APPROVAL REQUIRED[/]\n\n" +
            $"  {message.EscapeMarkup()}");

        var panel = new Panel(content)
            .Border(BoxBorder.Double)
            .BorderStyle(new Style(ConsoleTheme.Warning))
            .Padding(1, 1);

        AnsiConsole.Write(panel);

        // Optionally render the display value from a previous step
        if (!string.IsNullOrWhiteSpace(displayValue))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [{ConsoleTheme.MutedTag}]Preview:[/]");

            var previewPanel = new Panel(new Text(Truncate(displayValue, 2000)))
                .Border(BoxBorder.Rounded)
                .BorderStyle(new Style(ConsoleTheme.Subtle))
                .Padding(1, 0);

            AnsiConsole.Write(previewPanel);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Markup(
            $"  [{ConsoleTheme.PrimaryTag} bold]Continue?[/] " +
            $"[{ConsoleTheme.MutedTag}][[y/N]][/] ");

        while (true)
        {
            if (ct.IsCancellationRequested)
                return false;

            var input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";

            if (input is "y" or "yes" or "ok")
            {
                AnsiConsole.MarkupLine(
                    $"  [{ConsoleTheme.SuccessTag}]{ConsoleTheme.Check} Approved — continuing pipeline.[/]");
                AnsiConsole.WriteLine();
                return true;
            }

            if (input is "n" or "no" or "" )
            {
                AnsiConsole.MarkupLine(
                    $"  [{ConsoleTheme.ErrorTag}]{ConsoleTheme.Cross} Rejected — pipeline will stop.[/]");
                AnsiConsole.WriteLine();
                return false;
            }

            // Invalid input — re-prompt
            AnsiConsole.Markup(
                $"  [{ConsoleTheme.WarningTag}]Please type y (yes) or n (no):[/] ");
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────

    public void RenderError(string message)
    {
        AnsiConsole.MarkupLine($"  [{ConsoleTheme.ErrorTag}]{ConsoleTheme.Cross} {message.EscapeMarkup()}[/]");
    }

    public void RenderInfo(string message)
    {
        AnsiConsole.MarkupLine($"  [{ConsoleTheme.MutedTag}]{message.EscapeMarkup()}[/]");
    }

    private string FormatMetrics(TimeSpan elapsed, int tokens, double cost)
    {
        var duration = FormatDuration(elapsed);
        var parts = $"[{ConsoleTheme.MutedTag}]{duration}[/]";
        if (tokens > 0)
            parts += $"  [{ConsoleTheme.SubtleTag}]{tokens:N0} tokens[/]";
        if (cost > 0)
            parts += $"  [{ConsoleTheme.SubtleTag}]${cost:F4}[/]";
        return parts;
    }

    private static string FormatDuration(TimeSpan ts) => ts.TotalSeconds switch
    {
        < 1 => $"{ts.TotalMilliseconds:F0}ms",
        < 60 => $"{ts.TotalSeconds:F1}s",
        < 3600 => $"{ts.Minutes}m {ts.Seconds}s",
        _ => $"{ts.Hours}h {ts.Minutes}m"
    };

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "\u2026");
    }
}
