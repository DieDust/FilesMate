using System.IO;

namespace FilesMate.Core.Entries;

public enum FilterMatchKind
{
    Substring = 0,
    Prefix = 1,
    Extension = 2,
}

/// <summary>
/// In-memory filter over a published directory. Never requests filesystem metadata.
/// Thread-safety: immutable. Cancellation: applied by the caller rebuilding the view index.
/// </summary>
public sealed record EntryFilter
{
    public static EntryFilter None { get; } = new();

    public string Query { get; init; } = string.Empty;

    public FilterMatchKind Match { get; init; } = FilterMatchKind.Substring;

    public bool FilesOnly { get; init; }

    public bool DirectoriesOnly { get; init; }

    public bool IncludeHidden { get; init; } = true;

    public bool Matches(in FileEntryCore entry)
    {
        if (FilesOnly && entry.Kind != EntryKind.File)
        {
            return false;
        }

        if (DirectoriesOnly && entry.Kind != EntryKind.Directory)
        {
            return false;
        }

        if (!IncludeHidden && entry.Attributes.HasFlag(FileAttributes.Hidden))
        {
            return false;
        }

        if (string.IsNullOrEmpty(Query))
        {
            return true;
        }

        return Match switch
        {
            FilterMatchKind.Prefix => entry.Name.StartsWith(Query, StringComparison.OrdinalIgnoreCase),
            FilterMatchKind.Extension => HasExtension(entry.Name, Query),
            _ => entry.Name.Contains(Query, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static bool HasExtension(string name, string query)
    {
        var extension = Path.GetExtension(name);
        if (query.StartsWith('.'))
        {
            return extension.Equals(query, StringComparison.OrdinalIgnoreCase);
        }

        return extension.Equals("." + query, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(query, StringComparison.OrdinalIgnoreCase);
    }
}
