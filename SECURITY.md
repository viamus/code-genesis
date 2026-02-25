# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| 0.1.x   | Yes                |

## Reporting a Vulnerability

If you discover a security vulnerability in CodeGenesis, please report it responsibly.

**Do NOT open a public GitHub issue for security vulnerabilities.**

Instead, please send an email to the maintainers or use GitHub's [private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) feature on this repository.

### What to include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### Response timeline

- **Acknowledgment**: Within 48 hours
- **Initial assessment**: Within 1 week
- **Fix or mitigation**: Depends on severity, but we aim for 30 days for critical issues

## Security Considerations

CodeGenesis executes Claude Code CLI as a subprocess with access to the local filesystem. Keep the following in mind:

### Pipeline Configuration

- **Review YAML pipelines before running them.** Pipelines can specify `allowed_tools` that grant Claude access to tools like `Bash`, `Write`, and `Edit`. Only run pipelines from trusted sources.
- **Use `allowed_tools` to restrict access.** Limit each step to only the tools it needs. For example, a planning step should not need `Bash` access.
- **Set `timeout_seconds` appropriately.** This acts as a safety net, especially when using unlimited `max_turns`.

### Working Directory

- CodeGenesis operates within the specified `working_directory`. Be cautious when pointing it at sensitive directories.
- Avoid running pipelines with `working_directory` set to system-critical paths.

### API Keys

- Never commit API keys or secrets to the repository
- Use environment variables or secure secret management for credentials
- The `.gitignore` already excludes `.env`, `secrets.json`, and credential files

### Dependencies

- We regularly review and update dependencies for known vulnerabilities
- Run `dotnet list package --vulnerable` to check for known issues in your local copy
