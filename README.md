<div align="center">

# 🧬 CodeGenesis

[![CI](https://github.com/viamus/code-genesis/actions/workflows/ci.yml/badge.svg)](https://github.com/viamus/code-genesis/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![Claude Code](https://img.shields.io/badge/Claude_Code-Backend-orange?logo=anthropic)](https://docs.anthropic.com/en/docs/claude-code)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Wiki](https://img.shields.io/badge/Docs-Wiki-green?logo=github)](https://github.com/viamus/code-genesis/wiki)

**A .NET CLI engine that orchestrates multi-step AI pipelines using Claude Code as the execution backend.**

Define pipelines in YAML · Compose agents with Markdown bundles · Let Claude handle the rest

</div>

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/getting-started) installed and authenticated (`npm install -g @anthropic-ai/claude-code`)

## Quick Start

```bash
# Clone & build
git clone https://github.com/viamus/code-genesis.git && cd code-genesis
dotnet build

# Run a YAML pipeline
dotnet run --project CodeGenesis.Engine -- run-pipeline examples/hello-world.yml

# Run an ad-hoc task (Plan → Execute → Validate)
dotnet run --project CodeGenesis.Engine -- run "Add retry logic to the HttpClient service"
```

## Pipeline Example

```yaml
pipeline:
  name: "Code Review"

inputs:
  task:
    description: "What to review"

steps:
  - name: "Plan"
    prompt: "Create a review plan for: {{task}}"
    output_key: "plan"

  - name: "Execute"
    prompt: "Implement the plan: {{steps.plan}}"
    max_turns: 10
    output_key: "result"

  - name: "Validate"
    prompt: "Review: {{steps.result}}"
    optional: true
```

> Use `{{variable}}` for inputs and `{{steps.<key>}}` for outputs from previous steps.

## Documentation

Full documentation is available in the **[Wiki](https://github.com/viamus/code-genesis/wiki)**:

| | Page | |
|---|---|---|
| 🚀 | [Getting Started](https://github.com/viamus/code-genesis/wiki/Getting-Started) | Prerequisites, installation, first pipeline |
| 💻 | [CLI Reference](https://github.com/viamus/code-genesis/wiki/CLI-Reference) | `run` and `run-pipeline` commands |
| 📋 | [Pipeline YAML Reference](https://github.com/viamus/code-genesis/wiki/Pipeline-YAML-Reference) | YAML structure, template variables, max_turns |
| 🔀 | [Step Types](https://github.com/viamus/code-genesis/wiki/Step-Types) | Simple, Foreach, Parallel, ParallelForeach, Approval |
| 🔌 | [MCP Servers](https://github.com/viamus/code-genesis/wiki/MCP-Servers) | Custom tools via MCP stdio protocol |
| 📦 | [Context Bundles](https://github.com/viamus/code-genesis/wiki/Context-Bundles) | Reusable agent instruction packages |
| ⚙️ | [Configuration](https://github.com/viamus/code-genesis/wiki/Configuration) | Environment variables, appsettings.json, logs |
| 🏗️ | [Project Structure](https://github.com/viamus/code-genesis/wiki/Project-Structure) | Source tree and architecture |
| 🧪 | [Testing](https://github.com/viamus/code-genesis/wiki/Testing) | Test project, coverage, CI |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on how to contribute.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
