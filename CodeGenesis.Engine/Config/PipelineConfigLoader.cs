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

        ValidateStepEntries(config.Steps, "root");
    }

    private static void ValidateStepEntries(List<StepEntry> entries, string path)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var entryPath = $"{path}[{i}]";

            if (entry.IsForeach)
            {
                var fc = entry.Foreach!;
                if (string.IsNullOrWhiteSpace(fc.Collection))
                    throw new InvalidOperationException($"Foreach at {entryPath} is missing 'collection'.");
                if (fc.Steps.Count == 0)
                    throw new InvalidOperationException($"Foreach at {entryPath} must have at least one sub-step.");
                ValidateStepEntries(fc.Steps, $"{entryPath}.foreach");
            }
            else if (entry.IsParallelForeach)
            {
                var pfc = entry.ParallelForeach!;
                if (string.IsNullOrWhiteSpace(pfc.Collection))
                    throw new InvalidOperationException($"Parallel_foreach at {entryPath} is missing 'collection'.");
                if (pfc.Steps.Count == 0)
                    throw new InvalidOperationException($"Parallel_foreach at {entryPath} must have at least one sub-step.");
                ValidateStepEntries(pfc.Steps, $"{entryPath}.parallel_foreach");
            }
            else if (entry.IsParallel)
            {
                var pc = entry.Parallel!;
                if (pc.Branches.Count == 0)
                    throw new InvalidOperationException($"Parallel at {entryPath} must have at least one branch.");
                for (var b = 0; b < pc.Branches.Count; b++)
                {
                    var branch = pc.Branches[b];
                    if (string.IsNullOrWhiteSpace(branch.Name))
                        throw new InvalidOperationException($"Parallel branch {b + 1} at {entryPath} is missing a 'name'.");
                    if (branch.Steps.Count == 0)
                        throw new InvalidOperationException($"Parallel branch '{branch.Name}' at {entryPath} must have at least one step.");
                    ValidateStepEntries(branch.Steps, $"{entryPath}.parallel.{branch.Name}");
                }
            }
            else
            {
                // Simple step
                if (string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidOperationException($"Step at {entryPath} is missing a 'name'.");
                if (string.IsNullOrWhiteSpace(entry.Prompt) && string.IsNullOrWhiteSpace(entry.Context))
                    throw new InvalidOperationException($"Step '{entry.Name}' at {entryPath} must have either a 'prompt' or a 'context'.");
            }
        }
    }

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex TemplatePlaceholder();
}
