namespace FilesMate.Core.Entries;

/// <summary>Maps the alphabet to actual sorted rows, including repeated folder and file runs.</summary>
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

    public int Occurrences(string label) => _destinations.TryGetValue(label, out var candidates) ? candidates.Length : 0;

    public IReadOnlyList<int> Destinations(string label) => _destinations.TryGetValue(label, out var candidates) ? candidates : [];

    // A letter can have several runs within one kind when Latin and Chinese names
    // are sorted separately. Only different kinds need a choice in the UI.
    public bool HasBothKinds(string label)
    {
        if (!_destinations.TryGetValue(label, out var candidates)) return false;
        var hasFolders = false;
        var hasFiles = false;
        foreach (var section in candidates)
        {
            if (_sections[section].IsDirectory) hasFolders = true;
            else hasFiles = true;
            if (hasFolders && hasFiles) return true;
        }
        return false;
    }

    public int BoundaryDestination(string label, bool last)
        => _destinations.TryGetValue(label, out var candidates) ? (last ? candidates[^1] : candidates[0]) : -1;

    public int Destination(string label, int row, bool? isDirectory = null)
    {
        if (!_destinations.TryGetValue(label, out var candidates)) return -1;
        var active = SectionAt(row);
        if (active >= 0 && _sections[active].Label == label
            && (isDirectory is null || _sections[active].IsDirectory == isDirectory))
            return active;

        var nearest = -1;
        var distance = long.MaxValue;
        foreach (var section in candidates)
        {
            if (isDirectory is not null && _sections[section].IsDirectory != isDirectory) continue;
            var difference = Math.Abs((long)_sections[section].FirstIndex - row);
            if (difference >= distance) continue;
            nearest = section;
            distance = difference;
        }
        return nearest;
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
