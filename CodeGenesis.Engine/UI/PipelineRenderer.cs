using CodeGenesis.Engine.Pipeline;
using Spectre.Console;

namespace CodeGenesis.Engine.UI;

public sealed class PipelineRenderer
{
    // Shared spinner guard — only one Spectre.Console Status can run at a time
    private int _spinnerActive;

    // Deferred banner — RenderBanner() requests it, RenderPipelineStart() renders the combined version
    private bool _bannerPending;
    private bool _bannerRendered;

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

    private static readonly string BannerTitle =
        $"[{ConsoleTheme.PrimaryTag} bold]{ConsoleTheme.Spark} CodeGenesis Engine[/]  " +
        $"[{ConsoleTheme.SubtleTag}]v0.1.0[/]";

    private static readonly string BannerTagline =
        $"[{ConsoleTheme.MutedTag} italic]AI-powered pipeline orchestration[/]";

    /// <summary>Defers the banner — it will be rendered by <see cref="RenderPipelineStart"/>
    /// as a combined panel, or flushed standalone before errors.</summary>
    public void RenderBanner()
    {
        _bannerPending = true;
    }

    /// <summary>Flushes the standalone banner if it hasn't been rendered yet (e.g. before an error).</summary>
    private void EnsureBanner()
    {
        if (!_bannerPending || _bannerRendered) return;
        _bannerPending = false;
        _bannerRendered = true;

        AnsiConsole.WriteLine();

        var content = new Markup($"{BannerTitle}\n{BannerTagline}");
        var panel = new Panel(content)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(ConsoleTheme.Subtle))
            .Padding(2, 1);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    // ── Pipeline lifecycle ────────────────────────────────────────────

    public void RenderPipelineStart(PipelineContext context, int totalSteps)
    {
        // Suppress nested pipeline headers (e.g. inside foreach/parallel iterations)
        if (_depth.Value > 0 || IsSuppressed) return;

        _bannerPending = false;
        _bannerRendered = true;

        AnsiConsole.WriteLine();

        var lines = $"{BannerTitle}\n{BannerTagline}\n\n" +
                    $"[{ConsoleTheme.MutedTag}]Task[/]    {context.TaskDescription.EscapeMarkup()}\n" +
                    $"[{ConsoleTheme.MutedTag}]Steps[/]   {totalSteps}";

        if (context.WorkingDirectory is not null)
            lines += $"\n[{ConsoleTheme.MutedTag}]Dir[/]     {context.WorkingDirectory.EscapeMarkup()}";

        var panel = new Panel(new Markup(lines))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(ConsoleTheme.Subtle))
            .Padding(2, 1);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public void RenderPipelineSummary(PipelineContext context)
    {
        // Suppress nested pipeline summaries
        if (_depth.Value > 0 || IsSuppressed) return;

        AnsiConsole.WriteLine();

        var totalTokens = context.TotalInputTokens + context.TotalOutputTokens;
        var duration = FormatDuration(context.TotalDuration);

        var content = new Markup(
            $"[{ConsoleTheme.SuccessTag} bold]{ConsoleTheme.Check} Pipeline Complete[/]\n\n" +
            $"[{ConsoleTheme.MutedTag}]Duration[/]  {duration}\n" +
            $"[{ConsoleTheme.MutedTag}]Steps[/]     {context.StepsCompleted} completed\n" +
            $"[{ConsoleTheme.MutedTag}]Tokens[/]    {totalTokens:N0} [{ConsoleTheme.SubtleTag}]({context.TotalInputTokens:N0} in / {context.TotalOutputTokens:N0} out)[/]\n" +
            $"[{ConsoleTheme.MutedTag}]Cost[/]      ${context.TotalCostUsd:F4}");

        var panel = new Panel(content)
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(ConsoleTheme.Success))
            .Padding(2, 1);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public void RenderPipelineFailed(PipelineContext context)
    {
        // Suppress nested pipeline failure banners
        if (_depth.Value > 0 || IsSuppressed) return;

        AnsiConsole.WriteLine();

        var duration = FormatDuration(context.TotalDuration);

        var stepsInfo = $"[{ConsoleTheme.ErrorTag}]{context.StepsFailed} failed[/]" +
                        (context.StepsCompleted > 0 ? $"  [{ConsoleTheme.MutedTag}]{context.StepsCompleted} completed[/]" : "");

        var lines = $"[{ConsoleTheme.ErrorTag} bold]{ConsoleTheme.Cross} Pipeline Failed[/]\n\n" +
                    $"[{ConsoleTheme.MutedTag}]Duration[/]    {duration}\n" +
                    $"[{ConsoleTheme.MutedTag}]Steps[/]       {stepsInfo}";

        if (!string.IsNullOrWhiteSpace(context.FailedStepName))
            lines += $"\n[{ConsoleTheme.MutedTag}]Failed at[/]  [bold]{context.FailedStepName.EscapeMarkup()}[/]";

        if (!string.IsNullOrWhiteSpace(context.FailureReason))
            lines += $"\n\n[{ConsoleTheme.ErrorTag}]{context.FailureReason.EscapeMarkup()}[/]";

        var panel = new Panel(new Markup(lines))
            .Border(BoxBorder.Rounded)
            .BorderStyle(new Style(ConsoleTheme.Error))
            .Padding(2, 1);

        AnsiConsole.Write(panel);
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

    public async Task<StepResult> RunWithSpinner(string name, PipelineContext context, Func<Task<StepResult>> work)
    {
        // Skip spinner when suppressed or another spinner is already active
        // (Spectre.Console Status is a singleton — only one can run at a time)
        if (IsSuppressed || Interlocked.CompareExchange(ref _spinnerActive, 1, 0) != 0)
            return await work();

        StepResult result = null!;
        var savedStatusUpdate = context.StatusUpdate;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots2)
                .SpinnerStyle(new Style(ConsoleTheme.Primary))
                .StartAsync($"{Indent}[{ConsoleTheme.MutedTag}]Running {name.EscapeMarkup()}…[/]", async ctx =>
                {
                    // Wire context.StatusUpdate to update spinner text in real-time
                    context.StatusUpdate = msg =>
                    {
                        ctx.Status($"{Indent}[{ConsoleTheme.MutedTag}]{msg.EscapeMarkup()}[/]");
                    };

                    result = await work();
                });
        }
        finally
        {
            context.StatusUpdate = savedStatusUpdate;
            Interlocked.Exchange(ref _spinnerActive, 0);
        }
        return result;
    }

    /// <summary>
    /// Renders a thinking/tool-use line for parallel steps (no spinner available).
    /// Thread-safe — writes a single line with item label prefix.
    /// </summary>
    public void RenderThinking(string label, string message)
    {
        AnsiConsole.MarkupLine(
            $"{Indent}    [{ConsoleTheme.SubtleTag}]\U0001F4AD {label.EscapeMarkup()}: {message.EscapeMarkup()}[/]");
    }

    public void RenderStepSkipped(IPipelineStep step, int index, int total)
    {
        if (IsSuppressed) return;
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.SubtleTag}]{ConsoleTheme.Dash} Step {index}/{total}[/]  " +
            $"[{ConsoleTheme.SubtleTag}]{step.Name.EscapeMarkup()}[/]  " +
            $"[{ConsoleTheme.MutedTag}](cached)[/]");
    }

    public void RenderResumeHint(string pipelineFile)
    {
        if (IsSuppressed) return;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.SecondaryTag}]Tip:[/] Fix the issue, then resume with:");
        AnsiConsole.MarkupLine(
            $"  [{ConsoleTheme.PrimaryTag}]codegenesis run-pipeline {pipelineFile.EscapeMarkup()} --resume[/]");
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

    // ── Sub-pipeline ──────────────────────────────────────────────────

    public void RenderSubPipelineStart(string name, string path, int stepCount)
    {
        if (IsSuppressed) return;
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.PrimaryTag}]\U0001F4E6[/] " +
            $"[{ConsoleTheme.SecondaryTag}]sub-pipeline[/]  " +
            $"[bold]{name.EscapeMarkup()}[/]  " +
            $"[{ConsoleTheme.MutedTag}]{path.EscapeMarkup()}  ({stepCount} steps)[/]");
        AnsiConsole.WriteLine();
    }

    public void RenderSubPipelineComplete(string name, bool success, int stepCount, TimeSpan elapsed, int tokens, double cost)
    {
        if (IsSuppressed) return;
        var metrics = FormatMetrics(elapsed, tokens, cost);

        if (success)
        {
            AnsiConsole.MarkupLine(
                $"{Indent}  [{ConsoleTheme.SuccessTag}]\U0001F4E6 {stepCount}/{stepCount} completed[/]  {metrics}");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"{Indent}  [{ConsoleTheme.ErrorTag}]\U0001F4E6 {name.EscapeMarkup()} failed[/]  {metrics}");
        }
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

    // ── Parallel Live Table ─────────────────────────────────────────

    /// <summary>
    /// Runs parallel work with a live-updating table that shows per-item status.
    /// Replaces the chaotic interleaved console output with a clean in-place table.
    /// </summary>
    /// <param name="labels">Display labels for each parallel item.</param>
    /// <param name="stepType">Step type name (e.g. "parallel_foreach", "parallel").</param>
    /// <param name="detail">Detail text (e.g. "area_path  7 item(s)  concurrency: max 3").</param>
    /// <param name="work">Async delegate that runs the parallel work using the live table.</param>
    public async Task RunParallelWithLiveTable(
        IReadOnlyList<string> labels,
        string stepType,
        string detail,
        Func<ParallelLiveTable, Task> work)
    {
        AnsiConsole.MarkupLine(
            $"{Indent}[{ConsoleTheme.PrimaryTag}]\u26a1[/] " +
            $"[{ConsoleTheme.SecondaryTag}]{stepType.EscapeMarkup()}[/]  " +
            $"[{ConsoleTheme.MutedTag}]{detail.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        var liveTable = new ParallelLiveTable(labels);

        await AnsiConsole.Live(liveTable.Renderable)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async ctx =>
            {
                ctx.Refresh();

                // Periodic refresh to update elapsed timers for running items
                using var refreshCts = new CancellationTokenSource();
                var refreshTask = Task.Run(async () =>
                {
                    while (!refreshCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(1000, refreshCts.Token);
                            liveTable.Refresh();
                            ctx.Refresh();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, refreshCts.Token);

                await work(liveTable);

                await refreshCts.CancelAsync();
                try { await refreshTask; } catch (OperationCanceledException) { }

                // Final refresh to show completed state
                liveTable.Refresh();
                ctx.Refresh();
            });

        AnsiConsole.WriteLine();
    }

    public void RenderParallelSummary(int total, int succeeded, int failed)
    {
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
        EnsureBanner();
        AnsiConsole.MarkupLine($"  [{ConsoleTheme.ErrorTag}]{ConsoleTheme.Cross} {message.EscapeMarkup()}[/]");
    }

    public void RenderInfo(string message)
    {
        EnsureBanner();
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
