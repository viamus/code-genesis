# CLAUDE.md — CodeGenesis Developer Context

## What is CodeGenesis?

A .NET 10 CLI engine that orchestrates multi-step AI pipelines using Claude Code CLI as the execution backend. Define pipelines in YAML, compose agents with Markdown bundles, and Claude handles execution.

## Quick Commands

```bash
dotnet build                                    # Build
dotnet test                                     # Run tests
dotnet run --project CodeGenesis.Engine -- run-pipeline examples/hello-world.yml
dotnet run --project CodeGenesis.Engine -- run "Some task description"
```

## Project Structure

```
Solution.slnx                          # .NET 10, slnx format
CodeGenesis.Engine/                    # Main executable
  Program.cs                           # Entry point, DI setup (MS DI + Spectre.Console.Cli)
  Claude/                              # Claude Code CLI integration
    IClaudeRunner.cs                   # Interface: RunAsync(ClaudeRequest) -> ClaudeResponse
    ClaudeCliRunner.cs                 # Spawns `claude --print --verbose --output-format stream-json`
    ClaudeRequest.cs                   # Record: prompt, system prompt, model, max_turns, MCP servers
    ClaudeResponse.cs                  # Record: success, result text, tokens, cost, FailureKind enum
    ClaudeCliOptions.cs                # Config: CliPath, DefaultModel, TimeoutSeconds, MaxTurnsDefault
    ClaudeProgressEvent.cs             # Record: thinking/tool_use events from NDJSON stream
  Pipeline/                            # Execution engine
    IPipelineStep.cs                   # Interface: Name, Description, ExecuteAsync(context, ct)
    IStepExecutor.cs                   # Runs a list of steps (used by composite steps to recurse)
    PipelineExecutor.cs                # Main orchestrator, implements IStepExecutor
    PipelineContext.cs                 # Shared mutable state: StepOutputs, metrics, StatusUpdate callback
    StepResult.cs                      # Record: Success/Failed/Skipped + output + duration + tokens
    RetryPolicy.cs                     # Record: MaxRetries, backoff, rate-limit pauses
    CollectionParser.cs                # Parses step output into lists for foreach
  Steps/                               # Step type implementations
    DynamicStep.cs                     # Workhorse for YAML steps (retries, fail_if, optional, MCP)
    PlanStep.cs                        # Hardcoded "architect" prompt (for `run` command)
    ExecuteStep.cs                     # Hardcoded "engineer" prompt (for `run` command)
    ValidateStep.cs                    # Hardcoded "reviewer" prompt (for `run` command)
    ForeachStep.cs                     # Sequential iteration over collections
    ParallelStep.cs                    # Concurrent named branches (Task.WhenAll + SemaphoreSlim)
    ParallelForeachStep.cs             # Parallel iteration (clones DynamicStep for thread safety)
    ApprovalStep.cs                    # Interactive console pause
    StepBuilder.cs                     # Recursive factory: StepEntry -> IPipelineStep tree
  Config/                              # Configuration & loading
    PipelineConfig.cs                  # Full YAML deserialization model (YamlDotNet, snake_case aliases)
    PipelineConfigLoader.cs            # Load YAML, resolve {{templates}}, validate
    ContextBundleLoader.cs             # Load Markdown agent bundles (Claude Code-style or legacy)
    MarkdownFrontmatterParser.cs       # Parse YAML frontmatter from .md files
    AgentDefinition.cs                 # Agent config from bundles (model, tools, maxTurns)
    McpServerConfig.cs                 # MCP server definition (command, args, env, description, parameters)
    McpContextBuilder.cs               # Builds "Available MCP Tools" Markdown section for system prompt
  Cli/                                 # CLI commands (Spectre.Console.Cli)
    RunCommand.cs                      # `run <task>` — hardcoded Plan->Execute->Validate
    RunPipelineCommand.cs              # `run-pipeline <file>` — YAML-driven pipeline
    RunCommandSettings.cs              # Settings for `run`
    RunPipelineCommandSettings.cs      # Settings for `run-pipeline`
    TypeRegistrar.cs                   # Bridges MS DI into Spectre.Console.Cli
  UI/                                  # Terminal rendering
    PipelineRenderer.cs                # Spectre.Console output (spinners, panels, AsyncLocal depth)
    ConsoleTheme.cs                    # Colors (purple/blue/green/red/yellow) and symbols
CodeGenesis.Engine.Tests/              # xUnit test project
  Claude/ClaudeResponseTests.cs
  Config/McpServerConfigTests.cs
  Config/McpContextBuilderTests.cs
  Config/PipelineConfigLoaderTests.cs
  Config/StepEntryTests.cs
  Pipeline/CollectionParserTests.cs
  Pipeline/PipelineContextTests.cs
  Pipeline/RetryPolicyTests.cs
  Pipeline/StepResultTests.cs
  Steps/DynamicStepTests.cs
examples/                              # Example YAML pipelines
```

## Architecture: Two Execution Paths

**`run <task>`** — Hardcoded 3-step pipeline: PlanStep -> ExecuteStep -> ValidateStep (--skip-validate to skip last)

**`run-pipeline <file.yml>`** — YAML-driven pipeline:
1. `PipelineConfigLoader.LoadFromFile()` deserializes YAML + resolves static `{{input}}` templates
2. `StepBuilder.BuildAll()` creates recursive `IPipelineStep` tree
3. `PipelineExecutor.RunAsync()` executes steps; `onBeforeStep` callback re-resolves `{{steps.xxx}}` templates with latest outputs

## Template System

- `{{variable}}` for pipeline inputs
- `{{steps.<output_key>}}` for outputs from previous steps
- Inside foreach: `{{loop.item}}`, `{{loop.index}}`, `{{itemVar}}`
- Resolution happens twice: at build time (static inputs) and before each step (dynamic outputs)
- Unresolved placeholders are left as-is (not an error)

## Key Patterns & Conventions

- **C# 12+**: primary constructors, `sealed` classes by default, `record` types for value objects
- **Nullable enabled** throughout, `required` keyword where needed
- **Namespaces**: `CodeGenesis.Engine.<Subsystem>` (Claude, Pipeline, Steps, Config, Cli, UI)
- **YAML mapping**: snake_case in YAML -> PascalCase in C# via `[YamlMember(Alias = "snake_case")]`
- **Test naming**: `MethodName_Scenario_ExpectedBehavior`
- **Test stack**: xUnit + NSubstitute + FluentAssertions
- **Global usings**: `global using Xunit;` in test project

## Thread Safety

- `DynamicStep` is mutable (`UpdateResolvedPrompt()`). Sequential `ForeachStep` mutates shared instances safely.
- `ParallelForeachStep` **must clone** via `DynamicStep.Clone()` before dispatching to threads.
- `PipelineRenderer` uses `AsyncLocal<int>` for depth and `AsyncLocal<bool>` for suppression in parallel branches.

## Retry / Rate Limit Policy

- **Rate limit** (429/overloaded): pause `RateLimitPauseSeconds`, retry up to `MaxRateLimitPauses` (doesn't count as retry)
- **Timeout**: retry up to `MaxRetries` with exponential backoff
- **Other failures**: fail immediately
- Policy cascade: step-level > global pipeline settings > hardcoded defaults

## MCP Server Documentation

MCP servers support `description` and `parameters` fields (YAML-only metadata, not sent to Claude CLI JSON):

```yaml
mcp_servers:
  jira:
    command: "npx"
    args: ["-y", "@anthropic/mcp-jira"]
    description: "Search and manage Jira tickets"
    parameters:
      project_key:
        description: "The Jira project key"
        example: "PROJ-123"
```

`McpContextBuilder.Build()` generates a `## Available MCP Tools` Markdown section that is appended to the system prompt in `StepBuilder.BuildSimple()`. Servers without description or parameters are silently skipped.

## Key Behaviors

- `optional: true` converts `Failed` -> `Skipped` (pipeline continues)
- `fail_if:` is a post-success check (case-insensitive substring match on output)
- MCP config: temp files `codegenesis-mcp-{guid}.json`, always cleaned up in `finally`
- `Console.OutputEncoding = UTF8` set at startup for Unicode symbols on Windows

## Dependencies

| Package | Purpose |
|---|---|
| Spectre.Console / Spectre.Console.Cli | Rich terminal UI + CLI framework |
| Microsoft.Extensions.* (10.0 preview) | DI, configuration, logging |
| Serilog + File + Console sinks | Structured logging to `logs/` |
| YamlDotNet | YAML pipeline config parsing |
| xUnit + NSubstitute + FluentAssertions | Testing |

## CI

GitHub Actions on push/PR to `main`: restore -> build Release -> test Release (ubuntu-latest, .NET 10 preview).

## Adding a New Step Type

1. Implement `IPipelineStep` (sealed class with primary constructor)
2. Add discriminator field on `StepEntry` in `Config/PipelineConfig.cs`
3. Add branch in `StepBuilder.Build()`
4. Handle rendering in `PipelineRenderer` if needed
5. Add tests in `CodeGenesis.Engine.Tests/Steps/`
