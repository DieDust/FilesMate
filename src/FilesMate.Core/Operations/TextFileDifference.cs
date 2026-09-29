namespace FilesMate.Core.Operations;

public enum TextDifferenceKind { Equal, Changed, Omitted }

public sealed record TextDifferenceRow(int? IncomingLine, string Incoming, int? ExistingLine, string Existing,
    TextDifferenceKind Kind);

public sealed record TextDifferenceResult(IReadOnlyList<TextDifferenceRow> Rows, int AddedLines, int RemovedLines,
    bool IsLimited, bool IsSimplified);

/// <summary>A bounded, read-only line comparison. Large changed regions use a coarse alignment.</summary>
public static class TextFileDifference
{
    public const int MaxLines = 2000;
    private const int MaxCells = 2_000_000;
    private const int MaxLineLength = 4096;

    public static TextDifferenceResult Compare(string incoming, string existing, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var left = Split(incoming); var right = Split(existing);
        var limited = left.Length > MaxLines || right.Length > MaxLines
            || left.Any(line => line.Length > MaxLineLength) || right.Any(line => line.Length > MaxLineLength);
        left = left.Take(MaxLines).ToArray(); right = right.Take(MaxLines).ToArray();
        var prefix = 0;
        while (prefix < Math.Min(left.Length, right.Length) && left[prefix] == right[prefix]) prefix++;
        var suffix = 0;
        while (suffix < Math.Min(left.Length, right.Length) - prefix
            && left[^(suffix + 1)] == right[^(suffix + 1)]) suffix++;
        var rows = new List<TextDifferenceRow>();
        var added = 0; var removed = 0;
        void Equal(int l, int r) => rows.Add(new(l + 1, Display(left[l]), r + 1, Display(right[r]), TextDifferenceKind.Equal));
        void Changed(int l, int lc, int r, int rc)
        {
            added += lc; removed += rc;
            for (var i = 0; i < Math.Max(lc, rc); i++)
                rows.Add(new(i < lc ? l + i + 1 : null, i < lc ? Display(left[l + i]) : "",
                    i < rc ? r + i + 1 : null, i < rc ? Display(right[r + i]) : "", TextDifferenceKind.Changed));
        }
        for (var i = 0; i < prefix; i++) Equal(i, i);
        var m = left.Length - prefix - suffix; var n = right.Length - prefix - suffix;
        var simplified = (long)(m + 1) * (n + 1) > MaxCells;
        if (simplified || m == 0 || n == 0) Changed(prefix, m, prefix, n);
        else
        {
            var width = n + 1;
            var lengths = new int[(m + 1) * width];
            for (var i = m - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                for (var j = n - 1; j >= 0; j--)
                    lengths[i * width + j] = left[prefix + i] == right[prefix + j]
                        ? lengths[(i + 1) * width + j + 1] + 1
                        : Math.Max(lengths[(i + 1) * width + j], lengths[i * width + j + 1]);
            }
            var x = 0; var y = 0;
            while (x < m || y < n)
            {
                token.ThrowIfCancellationRequested();
                if (x < m && y < n && left[prefix + x] == right[prefix + y])
                { Equal(prefix + x++, prefix + y++); continue; }
                var fromX = x; var fromY = y;
                while (x < m || y < n)
                {
                    if (x < m && y < n && left[prefix + x] == right[prefix + y]) break;
                    if (x < m && (y == n || lengths[(x + 1) * width + y] >= lengths[x * width + y + 1])) x++;
                    else y++;
                }
                Changed(prefix + fromX, x - fromX, prefix + fromY, y - fromY);
            }
        }
        for (var i = suffix; i > 0; i--) Equal(left.Length - i, right.Length - i);
        token.ThrowIfCancellationRequested();
        return new(WithContext(rows), added, removed, limited, simplified);
    }

    private static string[] Split(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    private static string Display(string text) => text.Length <= MaxLineLength ? text : text[..MaxLineLength] + "…";

    private static IReadOnlyList<TextDifferenceRow> WithContext(List<TextDifferenceRow> rows)
    {
        var keep = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Kind != TextDifferenceKind.Equal)
                for (var j = Math.Max(0, i - 3); j < Math.Min(rows.Count, i + 4); j++) keep[j] = true;
        // Identical files still show the beginning of the content for orientation.
        if (!keep.Any(value => value)) Array.Fill(keep, true, 0, Math.Min(12, keep.Length));
        var result = new List<TextDifferenceRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (keep[i]) result.Add(rows[i]);
            else if (result.Count == 0 || result[^1].Kind != TextDifferenceKind.Omitted)
                result.Add(new(null, "…", null, "…", TextDifferenceKind.Omitted));
        }
        return result;
    }
}
