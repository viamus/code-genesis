namespace CodeGenesis.Engine.Config;

public static class MarkdownFrontmatterParser
{
    /// <summary>
    /// Parses a markdown file with optional YAML frontmatter (delimited by ---).
    /// Returns the extracted config fields and the remaining body.
    /// </summary>
    public static (AgentDefinition config, string body) Parse(string content)
    {
        var config = new AgentDefinition();

        if (string.IsNullOrWhiteSpace(content))
            return (config, string.Empty);

        var trimmed = content.TrimStart();

        if (!trimmed.StartsWith("---"))
            return (config, content.Trim());

        // Find closing ---
        var endIndex = trimmed.IndexOf("\n---", 3);
        if (endIndex < 0)
            return (config, content.Trim());

        var frontmatter = trimmed.Substring(3, endIndex - 3).Trim();
        var body = trimmed.Substring(endIndex + 4).Trim();

        // Parse frontmatter line by line (simple key: value)
        foreach (var rawLine in frontmatter.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var colonIdx = line.IndexOf(':');
            if (colonIdx <= 0)
                continue;

            var key = line.Substring(0, colonIdx).Trim().ToLowerInvariant();
            var value = line.Substring(colonIdx + 1).Trim();

            switch (key)
            {
                case "model":
                    config.Model = value;
                    break;
                case "tools":
                    config.AllowedTools = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    break;
                case "maxturns" or "max_turns":
                    if (int.TryParse(value, out var turns))
                        config.MaxTurns = turns;
                    break;
                case "prompt":
                    config.Prompt = value;
                    break;
            }
        }

        return (config, body);
    }
}
