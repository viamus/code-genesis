using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGenesis.Engine.Claude;

public sealed class ClaudeResponse
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? RawOutput { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Result { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public double? CostUsd { get; init; }
    public TimeSpan Duration { get; init; }

    public static ClaudeResponse FromJson(string json, TimeSpan duration)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = root.TryGetProperty("result", out var r) ? r.GetString() : null;
            var inputTokens = root.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
            var outputTokens = root.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
            var cost = root.TryGetProperty("cost_usd", out var c) ? c.GetDouble() : (double?)null;

            // Handle num_turns for logging
            var numTurns = root.TryGetProperty("num_turns", out var nt) ? nt.GetInt32() : 0;

            return new ClaudeResponse
            {
                Success = true,
                ExitCode = 0,
                RawOutput = json,
                Result = result,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsd = cost,
                Duration = duration
            };
        }
        catch (JsonException ex)
        {
            return new ClaudeResponse
            {
                Success = false,
                ExitCode = 0,
                RawOutput = json,
                ErrorMessage = $"Failed to parse Claude output: {ex.Message}",
                Duration = duration
            };
        }
    }

    public static ClaudeResponse Failure(string error, int exitCode, TimeSpan duration) => new()
    {
        Success = false,
        ErrorMessage = error,
        ExitCode = exitCode,
        Duration = duration
    };
}
