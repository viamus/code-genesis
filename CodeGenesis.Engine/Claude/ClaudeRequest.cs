using CodeGenesis.Engine.Config;

namespace CodeGenesis.Engine.Claude;

public sealed record ClaudeRequest
{
    /// <summary>Prompt sent via stdin (preferred for large prompts).</summary>
    public string? Prompt { get; init; }

    /// <summary>System prompt passed via --system-prompt flag.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Model override (e.g. "claude-sonnet-4-6").</summary>
    public string? Model { get; init; }

    /// <summary>Max agentic turns. Higher for steps that use tools.</summary>
    public int? MaxTurns { get; init; }

    /// <summary>Working directory for the Claude process.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Restrict which tools Claude can use.</summary>
    public List<string> AllowedTools { get; init; } = [];

    /// <summary>MCP stdio servers to launch alongside the Claude process.</summary>
    public Dictionary<string, McpServerConfig>? McpServers { get; init; }

    /// <summary>Callback invoked with progress events (thinking, tool use) during streaming.</summary>
    public Action<ClaudeProgressEvent>? OnProgress { get; init; }
}
