namespace FilesMate.App.Preview;

/// <summary>Bounds native editor layout work without discarding preview content.</summary>
public static class TextPreviewPages
{
    public static IReadOnlyList<string> Split(string content)
    {
        const int maxCharacters = 16_384;
        const int maxLines = 256;
        var pages = new List<string>();
        var start = 0;
        while (start < content.Length)
        {
            var end = Math.Min(start + maxCharacters, content.Length);
            var lines = 0;
            for (var i = start; i < end; i++)
            {
                if (content[i] == '\n' && ++lines == maxLines) { end = i + 1; break; }
            }
            // Preserve surrogate pairs and CRLF at page boundaries.
            if (end < content.Length && (char.IsHighSurrogate(content[end - 1]) ||
                (content[end - 1] == '\r' && content[end] == '\n'))) end--;
            pages.Add(content[start..end]);
            start = end;
        }
        if (pages.Count == 0) pages.Add(string.Empty);
        return pages;
    }
}
