# Contributing to CodeGenesis

Thank you for your interest in contributing to CodeGenesis! This document provides guidelines and instructions for contributing.

## Getting Started

1. Fork the repository
2. Clone your fork:
   ```bash
   git clone https://github.com/<your-username>/code-genesis.git
   cd code-genesis
   ```
3. Create a feature branch:
   ```bash
   git checkout -b feature/my-feature
   ```
4. Install prerequisites:
   - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
   - [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code/getting-started)

## Development Workflow

### Building

```bash
dotnet build
```

### Running

```bash
dotnet run --project CodeGenesis.Engine -- run-pipeline examples/hello-world.yml
```

### Testing

```bash
dotnet test
```

## Making Changes

### Code Style

- Follow the existing code conventions in the project
- Use `nullable` reference types (`<Nullable>enable</Nullable>`)
- Prefer records and primary constructors where appropriate
- Use meaningful names; avoid abbreviations

### Commit Messages

Write clear, concise commit messages:

- Use the imperative mood ("Add feature" not "Added feature")
- Keep the first line under 72 characters
- Reference issues when applicable (e.g., `Fix #42: handle null pipeline config`)

### Adding a New Pipeline Step

1. Create a class implementing `IPipelineStep` in `CodeGenesis.Engine/Steps/`
2. Register it in the appropriate command (`RunCommand` or via YAML config)
3. Add tests for the new step

### Adding a New CLI Command

1. Create a `CommandSettings` class in `CodeGenesis.Engine/Cli/`
2. Create the command class inheriting `AsyncCommand<TSettings>`
3. Register the command in `Program.cs`

## Pull Requests

1. Update documentation if your change affects user-facing behavior
2. Ensure the project builds without warnings
3. Keep PRs focused — one feature or fix per PR
4. Fill in the PR template with a clear description and test plan

## Reporting Issues

- Use GitHub Issues to report bugs or request features
- Include steps to reproduce for bugs
- Include the output of `dotnet --version` and `claude --version`

## Code of Conduct

This project follows our [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to uphold it.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
