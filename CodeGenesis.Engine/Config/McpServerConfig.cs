using YamlDotNet.Serialization;

namespace CodeGenesis.Engine.Config;

/// <summary>
/// Configuration for a single MCP stdio server.
/// Maps to the Claude CLI --mcp-config JSON format.
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
    /// Creates a deep clone with all template placeholders resolved.
    /// </summary>
    public McpServerConfig ResolveTemplates(Func<string, string> resolve) => new()
    {
        Command = resolve(Command),
        Args = Args.Select(resolve).ToList(),
        Env = Env.ToDictionary(kv => kv.Key, kv => resolve(kv.Value))
    };
}
