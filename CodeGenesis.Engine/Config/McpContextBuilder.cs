using System.Text;

namespace CodeGenesis.Engine.Config;

/// <summary>
/// Builds a Markdown "Available MCP Tools" section from MCP server metadata,
/// for injection into the system prompt.
/// </summary>
public static class McpContextBuilder
{
    /// <summary>
    /// Generates a Markdown section describing available MCP servers.
    /// Returns null when no server has a description or parameters (nothing to emit).
    /// </summary>
    public static string? Build(Dictionary<string, McpServerConfig> servers)
    {
        var documented = servers
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Description)
                         || kv.Value.Parameters is { Count: > 0 })
            .ToList();

        if (documented.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("## Available MCP Tools");
        sb.AppendLine();

        foreach (var (key, config) in documented)
        {
            sb.Append("### ").AppendLine(key);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(config.Description))
            {
                sb.AppendLine(config.Description.Trim());
                sb.AppendLine();
            }

            if (config.Parameters is { Count: > 0 })
            {
                sb.AppendLine("**Parameters:**");
                sb.AppendLine();

                foreach (var (paramName, paramConfig) in config.Parameters)
                {
                    var line = new StringBuilder($"- `{paramName}`");

                    if (!string.IsNullOrWhiteSpace(paramConfig.Description))
                        line.Append($": {paramConfig.Description.Trim()}");

                    if (!string.IsNullOrWhiteSpace(paramConfig.Example))
                        line.Append($" (example: `{paramConfig.Example.Trim()}`)");

                    sb.AppendLine(line.ToString());
                }

                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }
}
