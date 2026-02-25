using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeGenesis.Engine.Config;

public static partial class PipelineConfigLoader
{
    public static PipelineConfig LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Pipeline config not found: {path}", path);

        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var config = deserializer.Deserialize<PipelineConfig>(yaml)
            ?? throw new InvalidOperationException("Failed to deserialize pipeline config.");

        Validate(config);
        return config;
    }

    public static string ResolveTemplate(string template, Dictionary<string, string> variables)
    {
        return TemplatePlaceholder().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();

            if (variables.TryGetValue(key, out var value))
                return value;

            return match.Value; // Leave unresolved placeholders as-is
        });
    }

    private static void Validate(PipelineConfig config)
    {
        if (config.Steps.Count == 0)
            throw new InvalidOperationException("Pipeline must have at least one step.");

        for (var i = 0; i < config.Steps.Count; i++)
        {
            var step = config.Steps[i];

            if (string.IsNullOrWhiteSpace(step.Name))
                throw new InvalidOperationException($"Step {i + 1} is missing a 'name'.");

            if (string.IsNullOrWhiteSpace(step.Prompt) && string.IsNullOrWhiteSpace(step.Context))
                throw new InvalidOperationException($"Step '{step.Name}' must have either a 'prompt' or a 'context'.");
        }
    }

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex TemplatePlaceholder();
}
