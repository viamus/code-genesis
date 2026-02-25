namespace CodeGenesis.Engine.Claude;

public sealed class ClaudeCliOptions
{
    public string CliPath { get; set; } = "claude";
    public string? DefaultModel { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    /// <summary>
    /// Default max agentic turns per step. Set to 0 for unlimited turns.
    /// </summary>
    public int MaxTurnsDefault { get; set; } = 5;
}
