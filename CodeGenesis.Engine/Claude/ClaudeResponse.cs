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
            var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;
            var isError = root.TryGetProperty("is_error", out var ie) && ie.GetBoolean();

            // Tokens live inside the "usage" object
            var inputTokens = 0;
            var outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                inputTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                outputTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;

                // Include cache tokens in the input total for accurate tracking
                if (usage.TryGetProperty("cache_creation_input_tokens", out var cc))
                    inputTokens += cc.GetInt32();
                if (usage.TryGetProperty("cache_read_input_tokens", out var cr))
                    inputTokens += cr.GetInt32();
            }

            // Cost field is "total_cost_usd" in Claude CLI output
            var cost = root.TryGetProperty("total_cost_usd", out var c) ? c.GetDouble() : (double?)null;

            var numTurns = root.TryGetProperty("num_turns", out var nt) ? nt.GetInt32() : 0;

            // Claude CLI exits with code 0 even when max_turns is reached.
            // Detect via subtype or is_error so the pipeline fails fast instead of hanging.
            if (subtype == "error_max_turns" || isError)
            {
                var errorDetail = result ?? subtype ?? "unknown error";
                var errorMsg = subtype == "error_max_turns"
                    ? $"Step reached max_turns limit ({numTurns} turn(s) used) without completing. Increase max_turns or simplify the step."
                    : $"Claude returned an error: {errorDetail}";

                return new ClaudeResponse
                {
                    Success = false,
                    ExitCode = 0,
                    RawOutput = json,
                    ErrorMessage = errorMsg,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    CostUsd = cost,
                    Duration = duration
                };
            }

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
