namespace FilesMate.Core.Entries;

public readonly record struct NameMatchRange(int StartIndex, int Length);

/// <summary>Literal inline-filter matches in the displayed name, using the filter's ordinal case rule.</summary>
public static class NameMatchRanges
{
    public static IReadOnlyList<NameMatchRange> Find(string displayedName, string query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(displayedName)) return [];
        List<NameMatchRange>? ranges = null;
        var offset = 0;
        while (offset <= displayedName.Length - query.Length)
        {
            var start = displayedName.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            var end = start + query.Length;
            // TextRange indexes use UTF-16 offsets. Never split a displayed surrogate pair.
            var first = start > 0 && char.IsLowSurrogate(displayedName[start])
                && char.IsHighSurrogate(displayedName[start - 1]) ? start - 1 : start;
            if (end < displayedName.Length && char.IsLowSurrogate(displayedName[end])
                && char.IsHighSurrogate(displayedName[end - 1])) end++;
            (ranges ??= []).Add(new(first, end - first));
            offset = end;
        }
        return ranges is null ? [] : ranges;
    }
}
