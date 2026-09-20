using System.IO;

namespace FilesMate.Core.Entries;

/// <summary>
/// Immutable published directory entry. The directory root lives on the session; this value stores the name only.
/// Thread-safety: values are readonly after publication and may be copied freely.
/// Ownership: the publisher retains no mutable alias; callers must not assume the filesystem still matches.
/// Cancellation: none; this is a snapshot value.
/// Errors: none; absence of an entry is expressed by not publishing it.
/// Staleness: attributes, size, and timestamps may be outdated as soon as they are published.
/// </summary>
public readonly record struct FileEntryCore
{
    public FileEntryCore(
        int Id,
        string Name,
        ulong Size,
        long ModifiedUtcTicks,
        long CreatedUtcTicks,
        FileAttributes Attributes,
        EntryKind Kind)
    {
        if (Id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Id));
        }

        if (string.IsNullOrEmpty(Name))
        {
            throw new ArgumentException("Entry name is required.", nameof(Name));
        }

        if (Name.Contains('\0'))
        {
            throw new ArgumentException("Entry name contains a null character.", nameof(Name));
        }

        this.Id = Id;
        this.Name = Name;
        this.Size = Size;
        this.ModifiedUtcTicks = ModifiedUtcTicks;
        this.CreatedUtcTicks = CreatedUtcTicks;
        this.Attributes = Attributes;
        this.Kind = Kind;
    }

    public int Id { get; init; }

    public string Name { get; init; }

    public ulong Size { get; init; }

    public long ModifiedUtcTicks { get; init; }

    public long CreatedUtcTicks { get; init; }

    public long AccessedUtcTicks { get; init; }

    public FileAttributes Attributes { get; init; }

    public EntryKind Kind { get; init; }
}
