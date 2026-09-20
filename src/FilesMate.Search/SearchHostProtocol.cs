using System.Text;

namespace FilesMate.Search;

/// <summary>One bounded newline-delimited message per search-host connection.</summary>
public static class SearchHostProtocol
{
    public const int MaximumRequestCharacters = 4096;
    public const int MaximumReplyCharacters = 8192;

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
