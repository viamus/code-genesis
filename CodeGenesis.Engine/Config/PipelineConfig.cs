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
    public List<StepEntry> Steps { get; set; } = [];

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

    [YamlMember(Alias = "fail_if")]
    public string? FailIf { get; set; }

    [YamlMember(Alias = "fail_message")]
    public string? FailMessage { get; set; }
}

public sealed class PipelineOutput
{
    [YamlMember(Alias = "source")]
    public string? Source { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }
}

/// <summary>
/// Polymorphic step entry: exactly one of Name/Foreach/Parallel is set.
/// Simple steps have Name+Prompt; composite steps use Foreach or Parallel.
/// </summary>
public sealed class StepEntry
{
    // --- Simple step fields (same as StepConfig) ---
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "agent")]
    public string? Agent { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "system_prompt")]
    public string? SystemPrompt { get; set; }

    [YamlMember(Alias = "prompt")]
    public string? Prompt { get; set; }

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

    [YamlMember(Alias = "fail_if")]
    public string? FailIf { get; set; }

    [YamlMember(Alias = "fail_message")]
    public string? FailMessage { get; set; }

    // --- Composite step fields ---
    [YamlMember(Alias = "foreach")]
    public ForeachConfig? Foreach { get; set; }

    [YamlMember(Alias = "parallel")]
    public ParallelConfig? Parallel { get; set; }

    // --- Discriminators ---
    public bool IsSimpleStep => Foreach is null && Parallel is null && Name is not null;
    public bool IsForeach => Foreach is not null;
    public bool IsParallel => Parallel is not null;

    /// <summary>Converts a simple StepEntry to the legacy StepConfig model.</summary>
    public StepConfig ToStepConfig() => new()
    {
        Name = Name ?? "",
        Agent = Agent,
        Description = Description,
        SystemPrompt = SystemPrompt,
        Prompt = Prompt ?? "",
        Model = Model,
        Context = Context,
        MaxTurns = MaxTurns,
        OutputKey = OutputKey,
        AllowedTools = AllowedTools,
        Optional = Optional,
        FailIf = FailIf,
        FailMessage = FailMessage
    };
}

public sealed class ForeachConfig
{
    [YamlMember(Alias = "collection")]
    public string Collection { get; set; } = "";

    [YamlMember(Alias = "item_var")]
    public string ItemVar { get; set; } = "item";

    [YamlMember(Alias = "output_key")]
    public string? OutputKey { get; set; }

    [YamlMember(Alias = "steps")]
    public List<StepEntry> Steps { get; set; } = [];
}

public sealed class ParallelConfig
{
    [YamlMember(Alias = "max_concurrency")]
    public int? MaxConcurrency { get; set; }

    [YamlMember(Alias = "fail_fast")]
    public bool FailFast { get; set; }

    [YamlMember(Alias = "branches")]
    public List<ParallelBranch> Branches { get; set; } = [];
}

public sealed class ParallelBranch
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = "";

    [YamlMember(Alias = "output_key")]
    public string? OutputKey { get; set; }

    [YamlMember(Alias = "steps")]
    public List<StepEntry> Steps { get; set; } = [];
}
