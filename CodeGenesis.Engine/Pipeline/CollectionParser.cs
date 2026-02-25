using System.Text.Json;

namespace CodeGenesis.Engine.Pipeline;

/// <summary>
/// Parses a string into a list of items. Tries JSON array first,
/// then comma-separated, then newline-separated.
/// </summary>
public static class CollectionParser
{
    public static List<string> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var trimmed = input.Trim();

        // Try JSON array first
        if (trimmed.StartsWith('['))
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<JsonElement>>(trimmed);
                if (items is not null)
                    return items.Select(e => e.ValueKind == JsonValueKind.String
                        ? e.GetString()!
                        : e.GetRawText()).ToList();
            }
            catch (JsonException)
            {
                // Fall through to other formats
            }
        }

        // Try comma-separated (only if there are commas)
        if (trimmed.Contains(','))
        {
            return trimmed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // Try newline-separated
        if (trimmed.Contains('\n'))
        {
            return trimmed
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // Single item
        return [trimmed];
    }
}
