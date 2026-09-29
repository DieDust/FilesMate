namespace FilesMate.Core.Entries;

public enum EntryGrouping { FoldersFirst, FilesFirst, Mixed }

public enum EntrySortColumn
{
    Name = 0,
    Type = 1,
    Size = 2,
    Modified = 3,
    Created = 4,
    Extension = 5,
    Attributes = 6,
    Accessed = 7,
    Location = 8,
    FullPath = 9,
    ShellProperty = 10,
}

/// <summary>
/// Sort contract for a view index. Thread-safe as an immutable record. Does not touch the filesystem.
/// </summary>
public sealed record EntrySort
{
    public static EntrySort Name { get; } = new() { Column = EntrySortColumn.Name };

    public static EntrySort Size { get; } = new() { Column = EntrySortColumn.Size };

    public static EntrySort Type { get; } = new() { Column = EntrySortColumn.Type };

    public static EntrySort Modified { get; } = new() { Column = EntrySortColumn.Modified };

    public static EntrySort Created { get; } = new() { Column = EntrySortColumn.Created };

    public EntrySortColumn Column { get; init; } = EntrySortColumn.Name;

    public bool Ascending { get; init; } = true;

    public bool DirectoriesFirst { get; init; } = true;
    // Null preserves views saved before three-way grouping was available.
    public EntryGrouping? Grouping { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public EntryGrouping EffectiveGrouping => Grouping is { } grouping && Enum.IsDefined(grouping)
        ? grouping : DirectoriesFirst ? EntryGrouping.FoldersFirst : EntryGrouping.Mixed;
    public bool MixChineseAndLatin { get; init; }
    public string? PropertyName { get; init; }

    public EntrySort SelectColumn(EntrySortColumn column, bool defaultAscending = true, string? propertyName = null) =>
        this with
        {
            Column = column,
            PropertyName = column == EntrySortColumn.ShellProperty ? propertyName : null,
            Ascending = Column == column && (column != EntrySortColumn.ShellProperty || PropertyName == propertyName)
                ? !Ascending : defaultAscending,
        };
}
