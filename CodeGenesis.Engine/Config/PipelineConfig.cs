using YamlDotNet.Serialization;

namespace CodeGenesis.Engine.Config;

public sealed class PipelineConfig
{
    [YamlMember(Alias = "pipeline")]
    public PipelineMetadata Pipeline { get; set; } = new();

    [YamlMember(Alias = "settings")]
    public PipelineSettings Settings { get; set; } = new();

    [YamlMember(Alias = "inputs")]
    public Dictionary<string, PipelineInput> Inputs { get; set; } = new();

    [YamlMember(Alias = "steps")]
    public List<StepConfig> Steps { get; set; } = [];

    [YamlMember(Alias = "outputs")]
    public Dictionary<string, PipelineOutput> Outputs { get; set; } = new();
}

public sealed class PipelineMetadata
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "Unnamed Pipeline";

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "version")]
    public string? Version { get; set; }
}

public sealed class PipelineSettings
{
    [YamlMember(Alias = "model")]
    public string? Model { get; set; }

    [YamlMember(Alias = "max_turns")]
    public int? MaxTurns { get; set; }

    [YamlMember(Alias = "timeout_seconds")]
    public int? TimeoutSeconds { get; set; }

    [YamlMember(Alias = "working_directory")]
    public string? WorkingDirectory { get; set; }
}

public sealed class PipelineInput
{
    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "default")]
    public string? Default { get; set; }
}

public sealed class StepConfig
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "agent")]
    public string? Agent { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "system_prompt")]
    public string? SystemPrompt { get; set; }

    [YamlMember(Alias = "prompt")]
    public string Prompt { get; set; } = "";

    [YamlMember(Alias = "model")]
    public string? Model { get; set; }

    [YamlMember(Alias = "context")]
    public string? Context { get; set; }

    [YamlMember(Alias = "max_turns")]
    public int? MaxTurns { get; set; }

    [YamlMember(Alias = "output_key")]
    public string? OutputKey { get; set; }

    [YamlMember(Alias = "allowed_tools")]
    public List<string>? AllowedTools { get; set; }

    [YamlMember(Alias = "optional")]
    public bool Optional { get; set; }
}

public sealed class PipelineOutput
{
    [YamlMember(Alias = "source")]
    public string? Source { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }
}
