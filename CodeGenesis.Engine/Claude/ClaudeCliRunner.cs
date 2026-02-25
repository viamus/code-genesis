using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeGenesis.Engine.Claude;

public sealed class ClaudeCliRunner(
    IOptions<ClaudeCliOptions> options,
    ILogger<ClaudeCliRunner> logger) : IClaudeRunner
{
    private readonly ClaudeCliOptions _options = options.Value;

    public async Task<ClaudeResponse> RunAsync(ClaudeRequest request, CancellationToken ct = default)
    {
        var args = BuildArguments(request);
        logger.LogDebug("Launching: {CliPath} {Args}", _options.CliPath, args);

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
            await process.StandardInput.WriteAsync(request.Prompt);
            process.StandardInput.Close();
        }
        else
        {
            process.StandardInput.Close();
        }

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

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

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        sw.Stop();

        logger.LogDebug("Claude exited with code {Code} in {Duration:F1}s",
            process.ExitCode, sw.Elapsed.TotalSeconds);

        if (process.ExitCode != 0)
        {
            logger.LogError("Claude stderr: {Stderr}", stderr);
            return ClaudeResponse.Failure(stderr.Trim(), process.ExitCode, sw.Elapsed);
        }

        return ClaudeResponse.FromJson(stdout, sw.Elapsed);
    }

    private string BuildArguments(ClaudeRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("--print --output-format json");

        var model = request.Model ?? _options.DefaultModel;
        if (model is not null)
            sb.Append($" --model {model}");

        if (request.SystemPrompt is not null)
            sb.Append($" --system-prompt \"{Escape(request.SystemPrompt)}\"");

        // MaxTurns: null = use default, 0 = unlimited (omit flag), >0 = explicit limit
        if (request.MaxTurns is > 0)
            sb.Append($" --max-turns {request.MaxTurns}");

        foreach (var tool in request.AllowedTools)
            sb.Append($" --allowedTools \"{Escape(tool)}\"");

        return sb.ToString();
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
