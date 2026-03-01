using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeGenesis.Engine.Cli;

public sealed class RunPipelineCommandSettings : CommandSettings
{
    [CommandArgument(0, "<file>")]
    [Description("Path to the YAML pipeline configuration file")]
    public string File { get; set; } = "";

    [CommandOption("-i|--input <INPUT>")]
    [Description("Input override in key=value format (repeatable)")]
    public string[]? Input { get; set; }

    [CommandOption("-d|--directory")]
    [Description("Working directory for the pipeline (defaults to current directory)")]
    public string? Directory { get; set; }

    [CommandOption("-m|--model")]
    [Description("Claude model override (e.g. claude-sonnet-4-6)")]
    public string? Model { get; set; }

    [CommandOption("--resume")]
    [Description("Resume from the last checkpoint (skips completed steps)")]
    public bool Resume { get; set; }

    [CommandOption("--from-step <STEP>")]
    [Description("Resume from a specific step name (re-executes that step and all following)")]
    public string? FromStep { get; set; }

    public override ValidationResult Validate()
    {
        if (Resume && FromStep is not null)
            return ValidationResult.Error("--resume and --from-step are mutually exclusive.");

        return ValidationResult.Success();
    }
}
