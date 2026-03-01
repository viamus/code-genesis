using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeGenesis.Engine.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeGenesis.Engine.Claude;

public sealed class ClaudeCliRunner(
    IOptions<ClaudeCliOptions> options,
    ILogger<ClaudeCliRunner> logger) : IClaudeRunner
{
    private static readonly JsonSerializerOptions McpJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ClaudeCliOptions _options = options.Value;

    public async Task<ClaudeResponse> RunAsync(ClaudeRequest request, CancellationToken ct = default)
    {
        string? mcpConfigPath = null;
        try
        {
        mcpConfigPath = WriteMcpConfigFile(request.McpServers);

        var args = BuildArguments(request, mcpConfigPath);
        logger.LogDebug("Launching: {CliPath} {Args}", _options.CliPath, args);

        if (mcpConfigPath is not null)
            logger.LogDebug("MCP config written to: {Path}", mcpConfigPath);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _options.CliPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? Directory.GetCurrentDirectory()
        };

        var sw = Stopwatch.StartNew();

        process.Start();

        // Kill the process tree on cancellation
        ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        });

        // Pipe prompt via stdin
        if (request.Prompt is not null)
        {
            logger.LogDebug("Claude stdin (prompt):\n{Prompt}", request.Prompt);
            await process.StandardInput.WriteAsync(request.Prompt);
            process.StandardInput.Close();
        }
        else
        {
            process.StandardInput.Close();
        }

        // Read stderr concurrently to avoid deadlocks
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // Read stdout line-by-line, parsing stream-json events
        string? resultJson = null;
        try
        {
            resultJson = await ReadStreamEventsAsync(process.StandardOutput, request.OnProgress, ct);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation after cleanup
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Claude process timed out after {Timeout}s", _options.TimeoutSeconds);
            try { process.Kill(entireProcessTree: true); } catch { }
            return ClaudeResponse.Failure(
                $"Process timed out after {_options.TimeoutSeconds}s",
                -1,
                sw.Elapsed);
        }

        var stderr = await stderrTask;
        sw.Stop();

        logger.LogDebug("Claude exited with code {Code} in {Duration:F1}s",
            process.ExitCode, sw.Elapsed.TotalSeconds);

        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogDebug("Claude stderr:\n{Stderr}", stderr);

        if (!string.IsNullOrWhiteSpace(resultJson))
            logger.LogDebug("Claude result json:\n{Json}", resultJson);

        if (process.ExitCode != 0)
        {
            logger.LogError("Claude failed (exit {Code}): {Stderr}", process.ExitCode, stderr);
            return ClaudeResponse.Failure(stderr.Trim(), process.ExitCode, sw.Elapsed);
        }

        if (resultJson is null)
        {
            logger.LogError("Claude produced no result event in stream output");
            return ClaudeResponse.Failure("No result event in stream output", process.ExitCode, sw.Elapsed);
        }

        return ClaudeResponse.FromJson(resultJson, sw.Elapsed);
        }
        finally
        {
            if (mcpConfigPath is not null)
            {
                try { File.Delete(mcpConfigPath); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Reads stdout line-by-line, parsing stream-json events.
    /// Fires progress callbacks for thinking and tool_use events.
    /// Returns the final "result" JSON line.
    /// </summary>
    private async Task<string?> ReadStreamEventsAsync(
        System.IO.StreamReader stdout,
        Action<ClaudeProgressEvent>? onProgress,
        CancellationToken ct)
    {
        string? resultJson = null;

        while (!ct.IsCancellationRequested)
        {
            var line = await stdout.ReadLineAsync(ct);
            if (line is null) break; // EOF
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp))
                    continue;

                var type = typeProp.GetString();

                if (type == "result")
                {
                    resultJson = line;
                    continue;
                }

                if (type == "assistant")
                {
                    ProcessAssistantEvent(root, onProgress);
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines silently
                logger.LogTrace("Skipping non-JSON stream line: {Line}", line);
            }
        }

        return resultJson;
    }

    /// <summary>
    /// Extracts thinking and tool_use content from an assistant event, logs it, and fires progress callbacks.
    /// </summary>
    private void ProcessAssistantEvent(JsonElement root, Action<ClaudeProgressEvent>? onProgress)
    {
        if (!root.TryGetProperty("message", out var message))
            return;
        if (!message.TryGetProperty("content", out var content))
            return;
        if (content.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var itemType))
                continue;

            var itemTypeStr = itemType.GetString();

            if (itemTypeStr == "thinking")
            {
                var thinking = item.TryGetProperty("thinking", out var t) ? t.GetString() : null;
                if (!string.IsNullOrWhiteSpace(thinking))
                {
                    var summary = TruncateThinking(thinking!);
                    logger.LogDebug("Claude thinking: {Summary}", summary);
                    onProgress?.Invoke(new ClaudeProgressEvent(ClaudeProgressType.Thinking, summary));
                }
            }
            else if (itemTypeStr == "tool_use")
            {
                var toolName = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (toolName is not null)
                {
                    var summary = SummarizeToolUse(toolName, item);
                    logger.LogDebug("Claude tool_use: {Summary}", summary);
                    onProgress?.Invoke(new ClaudeProgressEvent(ClaudeProgressType.ToolUse, summary));
                }
            }
        }
    }

    /// <summary>Truncates thinking text to ~100 chars at the first sentence boundary.</summary>
    private static string TruncateThinking(string thinking)
    {
        // Clean up whitespace
        var clean = thinking.ReplaceLineEndings(" ").Trim();
        if (clean.Length <= 100) return clean;

        // Try to break at first sentence boundary within 100 chars
        var cutoff = clean.AsSpan(0, 100);
        var sentenceEnd = cutoff.LastIndexOfAny('.', '!', '?');
        if (sentenceEnd > 20)
            return clean[..(sentenceEnd + 1)];

        // Fall back to word boundary
        var spaceIdx = cutoff.LastIndexOf(' ');
        if (spaceIdx > 20)
            return string.Concat(clean.AsSpan(0, spaceIdx), "\u2026");

        return string.Concat(clean.AsSpan(0, 100), "\u2026");
    }

    /// <summary>Extracts a concise summary from a tool_use event (e.g. "Read: src/main.ts").</summary>
    private static string SummarizeToolUse(string toolName, JsonElement item)
    {
        if (!item.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
            return toolName;

        // Common tool patterns: extract the most useful argument
        if (input.TryGetProperty("file_path", out var filePath))
            return $"{toolName}: {filePath.GetString()}";
        if (input.TryGetProperty("path", out var path))
            return $"{toolName}: {path.GetString()}";
        if (input.TryGetProperty("command", out var command))
        {
            var cmd = command.GetString() ?? "";
            return $"{toolName}: {(cmd.Length > 60 ? string.Concat(cmd.AsSpan(0, 60), "\u2026") : cmd)}";
        }
        if (input.TryGetProperty("pattern", out var pattern))
            return $"{toolName}: {pattern.GetString()}";
        if (input.TryGetProperty("query", out var query))
            return $"{toolName}: {query.GetString()}";

        return toolName;
    }

    private string BuildArguments(ClaudeRequest request, string? mcpConfigPath = null)
    {
        var sb = new StringBuilder();
        sb.Append("--print --verbose --output-format stream-json");

        var model = request.Model ?? _options.DefaultModel;
        if (!string.IsNullOrWhiteSpace(model))
            sb.Append($" --model {model}");

        if (request.SystemPrompt is not null)
            sb.Append($" --system-prompt \"{Escape(request.SystemPrompt)}\"");

        // MaxTurns: null = use default, 0 = unlimited (omit flag), >0 = explicit limit
        if (request.MaxTurns is > 0)
            sb.Append($" --max-turns {request.MaxTurns}");

        foreach (var tool in request.AllowedTools)
            sb.Append($" --allowedTools \"{Escape(tool)}\"");

        if (mcpConfigPath is not null)
            sb.Append($" --mcp-config \"{Escape(mcpConfigPath)}\"");

        return sb.ToString();
    }

    /// <summary>
    /// Writes MCP server configuration to a temporary JSON file in the format
    /// expected by Claude CLI's --mcp-config flag.
    /// Returns the file path, or null if no MCP servers are configured.
    /// </summary>
    private static string? WriteMcpConfigFile(Dictionary<string, McpServerConfig>? mcpServers)
    {
        if (mcpServers is null || mcpServers.Count == 0)
            return null;

        var configObj = new Dictionary<string, object>
        {
            ["mcpServers"] = mcpServers.ToDictionary(
                kv => kv.Key,
                kv => (object)new
                {
                    command = kv.Value.Command,
                    args = kv.Value.Args,
                    env = kv.Value.Env.Count > 0 ? kv.Value.Env : null
                })
        };

        var json = JsonSerializer.Serialize(configObj, McpJsonOptions);
        var tempPath = Path.Combine(Path.GetTempPath(), $"codegenesis-mcp-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, json);
        return tempPath;
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
