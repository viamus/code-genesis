using System.ComponentModel;
using Spectre.Console.Cli;

namespace CodeGenesis.Engine.Cli;

public sealed class RunCommandSettings : CommandSettings
{
    [CommandArgument(0, "<task>")]
    [Description("The task description for Claude to execute")]
    public string Task { get; set; } = "";

    [CommandOption("-d|--directory")]
    [Description("Working directory for the pipeline (defaults to current directory)")]
    public string? Directory { get; set; }

    [CommandOption("-m|--model")]
    [Description("Claude model to use (e.g. claude-sonnet-4-6)")]
    public string? Model { get; set; }

    [CommandOption("--max-turns")]
    [Description("Maximum agentic turns per step")]
    [DefaultValue(5)]
    public int MaxTurns { get; set; } = 5;

    [CommandOption("--skip-validate")]
    [Description("Skip the validation step")]
    public bool SkipValidate { get; set; }
}
