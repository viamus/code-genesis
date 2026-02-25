using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeGenesis.Engine.Config;

public static class ContextBundleLoader
{
    public static AgentDefinition LoadBundle(string contextPath, string pipelineFileDir)
    {
        // Resolve relative paths against the pipeline file directory
        var fullPath = Path.IsPathRooted(contextPath)
            ? contextPath
            : Path.GetFullPath(Path.Combine(pipelineFileDir, contextPath));

        if (Directory.Exists(fullPath))
            return LoadFromDirectory(fullPath);

        // Try as-is, then with .yml/.yaml extensions
        if (File.Exists(fullPath))
            return LoadFromYamlFile(fullPath);

        if (File.Exists(fullPath + ".yml"))
            return LoadFromYamlFile(fullPath + ".yml");

        if (File.Exists(fullPath + ".yaml"))
            return LoadFromYamlFile(fullPath + ".yaml");

        throw new FileNotFoundException(
            $"Context bundle not found: '{contextPath}' (resolved to '{fullPath}').", fullPath);
    }

    private static AgentDefinition LoadFromYamlFile(string path)
    {
        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<AgentDefinition>(yaml)
            ?? new AgentDefinition();
    }

    private static AgentDefinition LoadFromDirectory(string dirPath)
    {
        // New Claude Code-style structure: CONTEXT.md + agents/*.md + skills/*/SKILL.md
        if (HasClaudeCodeStructure(dirPath))
            return LoadClaudeCodeBundle(dirPath);

        // Legacy fallback: numbered .md files + agent.yml
        return LoadLegacyBundle(dirPath);
    }

    private static bool HasClaudeCodeStructure(string dirPath)
    {
        var agentsDir = Path.Combine(dirPath, "agents");
        var skillsDir = Path.Combine(dirPath, "skills");
        return Directory.Exists(agentsDir) || Directory.Exists(skillsDir);
    }

    private static AgentDefinition LoadClaudeCodeBundle(string dirPath)
    {
        var promptParts = new List<string>();
        var result = new AgentDefinition();

        // 1. CONTEXT.md → general instructions
        var contextMdPath = Path.Combine(dirPath, "CONTEXT.md");
        if (File.Exists(contextMdPath))
        {
            var content = File.ReadAllText(contextMdPath).Trim();
            if (!string.IsNullOrEmpty(content))
                promptParts.Add(content);
        }

        // 2. agents/*.md → frontmatter (config) + body (system prompt)
        var agentsDir = Path.Combine(dirPath, "agents");
        if (Directory.Exists(agentsDir))
        {
            var agentFiles = Directory.GetFiles(agentsDir, "*.md")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var agentFile in agentFiles)
            {
                var raw = File.ReadAllText(agentFile);
                var (config, body) = MarkdownFrontmatterParser.Parse(raw);

                // Take config from first agent that defines each field
                result.Model ??= config.Model;
                result.MaxTurns ??= config.MaxTurns;
                result.AllowedTools ??= config.AllowedTools;
                result.Prompt ??= config.Prompt;

                if (!string.IsNullOrEmpty(body))
                    promptParts.Add(body);
            }
        }

        // 3. skills/*/SKILL.md → body as additional instructions
        var skillsDir = Path.Combine(dirPath, "skills");
        if (Directory.Exists(skillsDir))
        {
            var skillDirs = Directory.GetDirectories(skillsDir)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var skillDir in skillDirs)
            {
                var skillFile = Path.Combine(skillDir, "SKILL.md");
                if (!File.Exists(skillFile))
                    continue;

                var raw = File.ReadAllText(skillFile);
                var (_, body) = MarkdownFrontmatterParser.Parse(raw);

                if (!string.IsNullOrEmpty(body))
                    promptParts.Add(body);
            }
        }

        // 4. Compose system prompt
        if (promptParts.Count > 0)
            result.SystemPrompt = string.Join("\n\n", promptParts);

        return result;
    }

    private static AgentDefinition LoadLegacyBundle(string dirPath)
    {
        // Glob *.md files, sort alphabetically, concatenate → system_prompt
        var mdFiles = Directory.GetFiles(dirPath, "*.md")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mdContent = mdFiles.Count > 0
            ? string.Join("\n\n", mdFiles.Select(f => File.ReadAllText(f).TrimEnd()))
            : null;

        // Look for agent.yml or agent.yaml
        AgentDefinition agent;
        var agentYmlPath = Path.Combine(dirPath, "agent.yml");
        var agentYamlPath = Path.Combine(dirPath, "agent.yaml");

        if (File.Exists(agentYmlPath))
            agent = LoadFromYamlFile(agentYmlPath);
        else if (File.Exists(agentYamlPath))
            agent = LoadFromYamlFile(agentYamlPath);
        else
            agent = new AgentDefinition();

        // Combine: MDs are prepended to system_prompt from agent.yml
        if (mdContent is not null)
        {
            agent.SystemPrompt = agent.SystemPrompt is not null
                ? mdContent + "\n\n" + agent.SystemPrompt
                : mdContent;
        }

        return agent;
    }
}
