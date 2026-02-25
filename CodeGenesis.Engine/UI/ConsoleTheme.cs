using Spectre.Console;

namespace CodeGenesis.Engine.UI;

public static class ConsoleTheme
{
    // Brand colors — inspired by Claude's aesthetic
    public static readonly Color Primary = new(147, 130, 255);     // Soft purple
    public static readonly Color Secondary = new(100, 180, 255);   // Light blue
    public static readonly Color Success = new(80, 220, 120);      // Green
    public static readonly Color Error = new(255, 85, 85);         // Red
    public static readonly Color Warning = new(255, 200, 60);      // Yellow
    public static readonly Color Muted = new(120, 120, 140);       // Gray
    public static readonly Color Subtle = new(80, 80, 100);        // Dark gray
    public static readonly Color Text = new(220, 220, 230);        // Light text

    // Markup shortcuts
    public const string PrimaryTag = "rgb(147,130,255)";
    public const string SecondaryTag = "rgb(100,180,255)";
    public const string SuccessTag = "rgb(80,220,120)";
    public const string ErrorTag = "rgb(255,85,85)";
    public const string WarningTag = "rgb(255,200,60)";
    public const string MutedTag = "rgb(120,120,140)";
    public const string SubtleTag = "rgb(80,80,100)";

    // Symbols
    public const string Bullet = "\u25cf";     // ●
    public const string Arrow = "\u25b8";      // ▸
    public const string Check = "\u2714";      // ✔
    public const string Cross = "\u2718";      // ✘
    public const string Dash = "\u2500";       // ─
    public const string Dots = "\u2026";       // …
    public const string Spark = "\u2726";      // ✦
}
