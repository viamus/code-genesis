using YamlDotNet.Serialization;

namespace CodeGenesis.Engine.Config;

/// <summary>
/// Documents a parameter that an MCP server tool accepts.
/// This is YAML-only metadata injected into the system prompt;
/// it is not written to the --mcp-config JSON.
/// </summary>
public sealed class McpParameterConfig
{
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "example")]
    public string? Example { get; set; }
}

/// <summary>
/// Configuration for a single MCP stdio server.
/// Maps to the Claude CLI --mcp-config JSON format.
/// The <see cref="Description"/> and <see cref="Parameters"/> fields are
/// pipeline metadata only — they are injected into the system prompt so the
/// LLM knows when and how to use each tool.
/// </summary>
public sealed class McpServerConfig
{
    [YamlMember(Alias = "command")]
    public string Command { get; set; } = "";

    [YamlMember(Alias = "args")]
    public List<string> Args { get; set; } = [];

    [YamlMember(Alias = "env")]
    public Dictionary<string, string> Env { get; set; } = new();

    /// <summary>
    /// Human-readable description of what this MCP server does.
    /// Injected into the system prompt so the LLM knows when to use this tool.
    /// </summary>
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    /// <summary>
    /// Documents the parameters this server's tools accept.
    /// Key is parameter name; value carries description and optional example.
    /// </summary>
    [YamlMember(Alias = "parameters")]
    public Dictionary<string, McpParameterConfig>? Parameters { get; set; }

    /// <summary>
    /// Creates a deep clone with all template placeholders resolved,
    /// including description and parameter texts.
    /// </summary>
    public McpServerConfig ResolveTemplates(Func<string, string> resolve) => new()
    {
        Command = resolve(Command),
        Args = Args.Select(resolve).ToList(),
        Env = Env.ToDictionary(kv => kv.Key, kv => resolve(kv.Value)),
        Description = Description is not null ? resolve(Description) : null,
        Parameters = Parameters?.ToDictionary(
            kv => kv.Key,
            kv => new McpParameterConfig
            {
                Description = kv.Value.Description is not null ? resolve(kv.Value.Description) : null,
                Example = kv.Value.Example is not null ? resolve(kv.Value.Example) : null
            })
    };
}
