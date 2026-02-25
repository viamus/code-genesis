using YamlDotNet.Serialization;

namespace CodeGenesis.Engine.Config;

public sealed class AgentDefinition
{
    [YamlMember(Alias = "system_prompt")]
    public string? SystemPrompt { get; set; }

    [YamlMember(Alias = "prompt")]
    public string? Prompt { get; set; }

    [YamlMember(Alias = "model")]
    public string? Model { get; set; }

    [YamlMember(Alias = "max_turns")]
    public int? MaxTurns { get; set; }

    [YamlMember(Alias = "allowed_tools")]
    public List<string>? AllowedTools { get; set; }
}
