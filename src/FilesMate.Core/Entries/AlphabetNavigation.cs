namespace FilesMate.Core.Entries;

/// <summary>Maps the alphabet to actual sorted rows, independently of folder/language partitions.</summary>
public sealed class AlphabetNavigation
{
    public static IReadOnlyList<string> Labels { get; } = Array.AsReadOnly(
        new[] { "#" }.Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString())).Append("其他").ToArray());
    private readonly NameSection[] _sections;
    private readonly Dictionary<string, int[]> _destinations;
    public int Count { get; }
    public IReadOnlyList<NameSection> Sections { get; }

    public AlphabetNavigation(IReadOnlyList<NameSection> sections, int count)
    {
        _sections = sections.ToArray();
        Sections = Array.AsReadOnly(_sections);
        Count = Math.Max(0, count);
        _destinations = Enumerable.Range(0, _sections.Length).GroupBy(i => _sections[i].Label)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    public int SectionAt(int row)
    {
        if (_sections.Length == 0) return -1;
        var low = 0;
        var high = _sections.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (_sections[middle].FirstIndex <= row) low = middle; else high = middle - 1;
        }
        return low;
    }

    public bool Contains(string label) => _destinations.ContainsKey(label);

    public int Destination(string label, int row)
    {
        if (!_destinations.TryGetValue(label, out var candidates)) return -1;
        var active = SectionAt(row);
        if (active >= 0 && _sections[active].Label == label) return active;
        var low = 0;
        var high = candidates.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_sections[candidates[middle]].FirstIndex < row) low = middle + 1; else high = middle;
        }
        if (low == 0) return candidates[0];
        if (low == candidates.Length) return candidates[^1];
        return row - _sections[candidates[low - 1]].FirstIndex <= _sections[candidates[low]].FirstIndex - row
            ? candidates[low - 1] : candidates[low];
    }

    // Derive the leading item from the actual row geometry, never from scroll percentage.
    public int FirstVisibleIndex(double offset, double rowStride, int columns = 1)
    {
        columns = Math.Max(1, columns);
        var row = Math.Floor((Math.Max(0, offset) + .00001) / Math.Max(1, rowStride));
        return (int)Math.Clamp(row * columns, 0, Math.Max(0, Count - 1));
    }

    public double OffsetForItem(int index, double rowStride, int columns = 1) =>
        Math.Clamp(index, 0, Math.Max(0, Count - 1)) / Math.Max(1, columns) * Math.Max(1, rowStride);

    public (int Start, int End) Range(int section) => (uint)section >= (uint)_sections.Length ? (0, 0)
        : (_sections[section].FirstIndex, section + 1 < _sections.Length ? _sections[section + 1].FirstIndex : Count);
}
