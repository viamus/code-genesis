# CodeGenesis

A .NET CLI engine that orchestrates multi-step AI pipelines using [Claude Code](https://docs.anthropic.com/en/docs/claude-code) as the execution backend. Define pipelines in YAML, compose agents with Markdown context bundles, and let Claude handle planning, execution, and validation.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/getting-started) installed and authenticated
- An [Anthropic API key](https://console.anthropic.com/) configured for Claude Code

### Installing Claude Code

```bash
npm install -g @anthropic-ai/claude-code
```

After installing, authenticate:

```bash
claude login
```

Verify that it works:

```bash
claude --version
```

> For detailed setup instructions, see the [Claude Code documentation](https://docs.anthropic.com/en/docs/claude-code/getting-started).

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/your-org/code-genesis-github.git
cd code-genesis-github
```

### 2. Build the project

```bash
dotnet build
```

### 3. Run the hello-world pipeline

```bash
dotnet run --project CodeGenesis.Engine -- run-pipeline examples/hello-world.yml
```

Override input variables from the command line:

```bash
dotnet run --project CodeGenesis.Engine -- run-pipeline examples/hello-world.yml \
  --input task="Create a Python calculator" \
  --input language="python"
```

### 4. Run a quick ad-hoc task (hardcoded pipeline)

```bash
dotnet run --project CodeGenesis.Engine -- run "Add retry logic to the HttpClient service"
```

## CLI Reference

### `run <task>`

Runs a built-in **Plan > Execute > Validate** pipeline for a given task description.

| Option              | Description                              |
|---------------------|------------------------------------------|
| `-d, --directory`   | Working directory (default: current)     |
| `-m, --model`       | Claude model (e.g. `claude-sonnet-4-6`)  |
| `--max-turns`       | Max agentic turns per step (default: 5, 0 = unlimited) |
| `--skip-validate`   | Skip the validation step                 |

### `run-pipeline <file>`

Runs a pipeline defined in a YAML configuration file.

| Option              | Description                                     |
|---------------------|-------------------------------------------------|
| `-i, --input`       | Input override as `key=value` (repeatable)       |
| `-d, --directory`   | Working directory (default: current)             |
| `-m, --model`       | Model override for all steps                     |

## Pipeline YAML Reference

Pipelines are defined in YAML files with four sections:

```yaml
# ── Metadata ──────────────────────────────────────────
pipeline:
  name: "My Pipeline"
  description: "What this pipeline does"
  version: "1.0"

# ── Global settings ──────────────────────────────────
settings:
  model: "claude-sonnet-4-6"       # default model for all steps
  max_turns: 5                      # default max turns
  timeout_seconds: 300              # process timeout
  working_directory: "."            # working dir for Claude

# ── Input variables ──────────────────────────────────
inputs:
  task:
    description: "What to build"
    default: "Create a hello world page"
  language:
    description: "Target language"
    default: "html"

# ── Steps ─────────────────────────────────────────────
steps:
  - name: "Plan"
    agent: "architect"
    description: "Create implementation plan"
    model: "claude-opus-4-6"        # per-step model override
    context: "contexts/planner"     # load a context bundle
    output_key: "plan"              # store output for later steps

  - name: "Execute"
    agent: "engineer"
    description: "Implement the plan"
    system_prompt: "You are a senior engineer."
    prompt: |
      Implement this plan:
      {{steps.plan}}
      Original task: {{task}}
    max_turns: 10
    output_key: "result"
    allowed_tools:
      - "Read"
      - "Write"
      - "Edit"
      - "Bash"

  - name: "Validate"
    agent: "reviewer"
    description: "Review the implementation"
    prompt: |
      Review: {{steps.result}}
      Verdict: PASS or FAIL
    optional: true                  # pipeline continues if this fails

# ── Outputs ───────────────────────────────────────────
outputs:
  summary:
    source: "steps.result"
    description: "Final result"
```

### Template Variables

Use `{{variable}}` to reference inputs and `{{steps.<output_key>}}` to reference outputs from previous steps. Variables are resolved just before each step runs, so later steps always see the latest outputs.

### Understanding `max_turns`

A **turn** is one complete round-trip between CodeGenesis and Claude: the engine sends a prompt, Claude reasons about it, optionally calls tools (Read, Write, Edit, Bash, etc.), and returns a response. Complex tasks often require multiple turns — for example, Claude might read a file in turn 1, edit it in turn 2, and run tests in turn 3.

`max_turns` limits how many of these round-trips a single step is allowed to perform. This controls both cost and execution time:

| Value | Behavior |
|-------|----------|
| `0`   | **Unlimited**: Claude runs with no turn limit — it will keep working until the task is complete or the timeout is reached. Use this for complex, open-ended tasks where you want full autonomy. |
| `1`   | Single-shot: Claude responds once with no tool use. Good for planning or review steps that only need to produce text. |
| `3-5` | Light agentic work: enough for reading a few files and producing a response. This is the default. |
| `10+` | Deep agentic work: Claude can explore the codebase, create/edit multiple files, run commands, and iterate. Use for execution-heavy steps. |

You can set it at three levels (most specific wins):

1. **Per step** — `max_turns: 10` in a YAML step or agent frontmatter
2. **Per pipeline** — `settings.max_turns: 5` in the YAML global settings
3. **Global default** — `MaxTurnsDefault` in `appsettings.json` or via `--max-turns` CLI flag (default: `5`)

**Unlimited turns from the CLI:**

```bash
# Ad-hoc command with unlimited turns
dotnet run --project CodeGenesis.Engine -- run "Build a full REST API" --max-turns 0

# YAML pipeline with unlimited turns on a specific step
steps:
  - name: "Execute"
    max_turns: 0    # let Claude work until done
```

> **Warning:** Unlimited turns can consume significant API credits and run for a long time. Make sure `timeout_seconds` is configured appropriately to act as a safety net.

## Context Bundles

Context bundles let you package agent instructions as reusable Markdown directories instead of inlining everything in YAML. A step references a bundle via the `context` field:

```yaml
steps:
  - name: "Plan"
    context: "contexts/planner"
```

### Directory Structure

```
contexts/planner/
  CONTEXT.md                  # General instructions (loaded first)
  agents/
    architect.md              # Agent definition with frontmatter
  skills/
    plan-format/
      SKILL.md                # Additional skill instructions
```

### Agent Frontmatter

Agent `.md` files support YAML frontmatter for configuration:

```markdown
---
model: claude-opus-4-6
tools: Read, Grep, Glob
maxTurns: 1
prompt: "Analyze and plan the following task:\nTask: {{task}}"
---

# Software Architect

You are a senior software architect...
```

The Markdown body becomes the system prompt. Frontmatter fields (`model`, `tools`, `maxTurns`, `prompt`) override pipeline-level settings.

### Loading Priority

When a context bundle is loaded, values are resolved in this order (first defined wins):

1. Agent frontmatter (`agents/*.md`)
2. Pipeline YAML step config
3. Pipeline YAML global settings

## Configuration

### Environment Variables

All configuration can be set via environment variables prefixed with `CODEGENESIS_`:

```bash
export CODEGENESIS_Claude__CliPath="/usr/local/bin/claude"
export CODEGENESIS_Claude__DefaultModel="claude-sonnet-4-6"
export CODEGENESIS_Claude__TimeoutSeconds=600
```

### appsettings.json

Place an `appsettings.json` in the Engine project for persistent configuration:

```json
{
  "Claude": {
    "CliPath": "claude",
    "DefaultModel": "claude-sonnet-4-6",
    "TimeoutSeconds": 300,
    "MaxTurnsDefault": 5
  }
}
```

| Key               | Default              | Description                          |
|-------------------|----------------------|--------------------------------------|
| `CliPath`         | `"claude"`           | Path to the Claude Code CLI binary   |
| `DefaultModel`    | `null`               | Model used when none is specified    |
| `TimeoutSeconds`  | `300`                | Max seconds before killing a process |
| `MaxTurnsDefault` | `5`                  | Default agentic turns per step (0 = unlimited) |

## Project Structure

```
code-genesis-github/
  Solution.slnx
  examples/
    hello-world.yml                  # Sample pipeline
    contexts/
      planner/                       # Sample context bundle
        CONTEXT.md
        agents/architect.md
        skills/plan-format/SKILL.md
  CodeGenesis.Engine/
    Program.cs                       # Entry point, DI, Serilog, Spectre CLI
    Claude/
      ClaudeCliRunner.cs             # Spawns `claude --print` processes
      ClaudeCliOptions.cs            # Configuration POCO
      ClaudeRequest.cs               # Request model
      ClaudeResponse.cs              # Response model
      IClaudeRunner.cs               # Abstraction for testability
    Cli/
      RunCommand.cs                  # `run` command (hardcoded pipeline)
      RunPipelineCommand.cs          # `run-pipeline` command (YAML-driven)
    Config/
      PipelineConfig.cs              # YAML deserialization models
      PipelineConfigLoader.cs        # YAML loader + template resolver
      ContextBundleLoader.cs         # Loads context bundles from directories
      AgentDefinition.cs             # Agent config model
      MarkdownFrontmatterParser.cs   # Parses YAML frontmatter from .md files
    Pipeline/
      PipelineExecutor.cs            # Runs steps sequentially
      PipelineContext.cs             # Shared state between steps
      IPipelineStep.cs               # Step interface
      StepResult.cs                  # Step output model
    Steps/
      PlanStep.cs                    # Built-in planning step
      ExecuteStep.cs                 # Built-in execution step
      ValidateStep.cs                # Built-in validation step
      DynamicStep.cs                 # YAML-driven dynamic step
    UI/
      PipelineRenderer.cs            # Spectre.Console output
      ConsoleTheme.cs                # Theme constants
```

## Logs

Logs are written to the `logs/` directory with daily rolling files:

```
logs/codegenesis-20260225.log
```

Log level is `Debug` by default. Logs include timestamps, Claude process invocations, exit codes, and durations.

## Useful Links

- [Claude Code Documentation](https://docs.anthropic.com/en/docs/claude-code)
- [Claude Code CLI Reference](https://docs.anthropic.com/en/docs/claude-code/cli-reference)
- [Claude Code SDK (Sub-agents)](https://docs.anthropic.com/en/docs/claude-code/sdk)
- [Claude Models Overview](https://docs.anthropic.com/en/docs/about-claude/models)
- [Claude Code Best Practices](https://docs.anthropic.com/en/docs/claude-code/best-practices)
- [Prompt Engineering Guide](https://docs.anthropic.com/en/docs/build-with-claude/prompt-engineering)
- [Anthropic Cookbook](https://github.com/anthropics/anthropic-cookbook)
- [Spectre.Console Documentation](https://spectreconsole.net/)
- [YamlDotNet](https://github.com/aaubry/YamlDotNet)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
