using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CodeGenesis.Engine.UI;

/// <summary>
/// Thread-safe live table that displays parallel execution progress.
/// Each item is a row with status icon, elapsed time, item label, and current activity.
/// Updates in-place using Spectre.Console's Live rendering.
/// </summary>
public sealed class ParallelLiveTable
{
    private readonly object _lock = new();
    private readonly ItemState[] _items;
    private readonly Table _table;

    public ParallelLiveTable(IReadOnlyList<string> labels, int maxLabelWidth = 40)
    {
        _items = new ItemState[labels.Count];
        for (var i = 0; i < labels.Count; i++)
        {
            _items[i] = new ItemState
            {
                Label = Truncate(labels[i], maxLabelWidth),
                Status = ItemStatus.Waiting
            };
        }

        _table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(ConsoleTheme.Subtle))
            .HideHeaders()
            .AddColumn(new TableColumn(string.Empty).Width(10).NoWrap())
            .AddColumn(new TableColumn(string.Empty).NoWrap())
            .AddColumn(new TableColumn(string.Empty).NoWrap());

        RebuildRows();
    }

    public IRenderable Renderable => _table;

    public void MarkStarted(int index)
    {
        lock (_lock)
        {
            ref var item = ref _items[index];
            item.Status = ItemStatus.Running;
            item.Activity = "starting\u2026";
            item.StartedAt = Stopwatch.GetTimestamp();
        }
    }

    public void UpdateActivity(int index, string message)
    {
        lock (_lock)
        {
            ref var item = ref _items[index];
            item.Activity = Truncate(message, 60);
        }
    }

    public void MarkComplete(int index, bool success, TimeSpan elapsed, int tokens, double cost)
    {
        lock (_lock)
        {
            ref var item = ref _items[index];
            item.Status = success ? ItemStatus.Success : ItemStatus.Failed;
            item.Elapsed = elapsed;
            item.Tokens = tokens;
            item.Cost = cost;
            item.Activity = FormatMetrics(elapsed, tokens, cost);
        }
    }

    /// <summary>
    /// Rebuilds all table rows from current state. Call inside Live refresh.
    /// </summary>
    public void Refresh()
    {
        lock (_lock)
        {
            RebuildRows();
        }
    }

    private void RebuildRows()
    {
        _table.Rows.Clear();

        foreach (ref var item in _items.AsSpan())
        {
            // Update elapsed for running items
            if (item.Status == ItemStatus.Running && item.StartedAt > 0)
                item.Elapsed = Stopwatch.GetElapsedTime(item.StartedAt);

            var (icon, statusColor) = item.Status switch
            {
                ItemStatus.Waiting => ("\u25cc", ConsoleTheme.MutedTag),
                ItemStatus.Running => ("\u25cb", ConsoleTheme.SecondaryTag),
                ItemStatus.Success => (ConsoleTheme.Check, ConsoleTheme.SuccessTag),
                ItemStatus.Failed => (ConsoleTheme.Cross, ConsoleTheme.ErrorTag),
                _ => ("?", ConsoleTheme.MutedTag)
            };

            var statusText = item.Status switch
            {
                ItemStatus.Waiting => $"  [{statusColor}]{icon}[/]",
                ItemStatus.Running => $"[{statusColor}]{icon}[/] [{ConsoleTheme.SubtleTag}]{FormatDuration(item.Elapsed),6}[/]",
                _ => $"[{statusColor}]{icon}[/] [{ConsoleTheme.MutedTag}]{FormatDuration(item.Elapsed),6}[/]"
            };

            var labelColor = item.Status switch
            {
                ItemStatus.Success => ConsoleTheme.SuccessTag,
                ItemStatus.Failed => ConsoleTheme.ErrorTag,
                ItemStatus.Running => "bold",
                _ => ConsoleTheme.MutedTag
            };

            var activityColor = item.Status switch
            {
                ItemStatus.Success => ConsoleTheme.SubtleTag,
                ItemStatus.Failed => ConsoleTheme.ErrorTag,
                _ => ConsoleTheme.SubtleTag
            };

            var activityText = item.Status == ItemStatus.Waiting
                ? $"[{ConsoleTheme.MutedTag} italic]waiting[/]"
                : $"[{activityColor}]{(item.Activity ?? "").EscapeMarkup()}[/]";

            _table.AddRow(
                new Markup(statusText),
                new Markup($"[{labelColor}]{item.Label.EscapeMarkup()}[/]"),
                new Markup(activityText));
        }
    }

    private static string FormatMetrics(TimeSpan elapsed, int tokens, double cost)
    {
        var parts = new List<string>();
        parts.Add(FormatDuration(elapsed));
        if (tokens > 0) parts.Add($"{tokens:N0} tokens");
        if (cost > 0) parts.Add($"${cost:F4}");
        return string.Join("  ", parts);
    }

    private static string FormatDuration(TimeSpan ts) => ts.TotalSeconds switch
    {
        < 1 => $"{ts.TotalMilliseconds:F0}ms",
        < 60 => $"{ts.TotalSeconds:F0}s",
        < 3600 => $"{ts.Minutes}m {ts.Seconds:D2}s",
        _ => $"{ts.Hours}h {ts.Minutes:D2}m"
    };

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "\u2026");
    }

    private struct ItemState
    {
        public string Label;
        public ItemStatus Status;
        public string? Activity;
        public long StartedAt;
        public TimeSpan Elapsed;
        public int Tokens;
        public double Cost;
    }

    private enum ItemStatus
    {
        Waiting,
        Running,
        Success,
        Failed
    }
}
