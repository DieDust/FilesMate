using System.Text;
using System.Text.Json;

namespace FilesMate.Search;

/// <summary>One bounded newline-delimited message per search-host connection.</summary>
public static class SearchHostProtocol
{
    public const int MaximumRequestCharacters = 4096;
    public const int MaximumReplyCharacters = 8192;

    public static bool TryReadRequest(string message, out SearchHostRequest? request)
    {
        request = null;
        if (message is null || message.Length > MaximumRequestCharacters) return false;
        try
        {
            using var document = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            HashSet<string> keys = new(StringComparer.Ordinal);
            if (root.EnumerateObject().Any(property => !keys.Add(property.Name))
                || !root.TryGetProperty("Command", out var command) || command.ValueKind != JsonValueKind.String
                || command.GetString() is not { Length: > 0 } name) return false;
            // Classify only after duplicate-key rejection: last-key-wins deserialization must
            // never disguise an appearance envelope as a generic settings or manager update.
            if (name == "appearance-preview") return AppearancePreviewProtocol.TryRead(message, out request);
            request = JsonSerializer.Deserialize<SearchHostRequest>(message);
            return request is not null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return false; }
    }

    public static async Task<string?> ReadAsync(TextReader reader, int maximumCharacters, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        var message = new StringBuilder(Math.Min(maximumCharacters, 256));
        var buffer = new char[256];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0)
            {
                if (message.Length == 0) return null;
                throw new EndOfStreamException("Incomplete search-host message.");
            }
            var newline = Array.IndexOf(buffer, '\n', 0, count);
            message.Append(buffer, 0, newline < 0 ? count : newline);
            if (newline >= 0)
            {
                if (message.Length > 0 && message[^1] == '\r') message.Length--;
                if (message.Length > maximumCharacters) throw new InvalidDataException("Search-host message is too large.");
                return message.ToString();
            }
            // Allow one trailing CR while waiting for the LF, but never an unbounded line.
            if (message.Length > maximumCharacters && (message.Length != maximumCharacters + 1 || message[^1] != '\r'))
                throw new InvalidDataException("Search-host message is too large.");
        }
    }
}
