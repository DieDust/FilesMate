namespace FilesMate.Core.Entries;

/// <summary>
/// Classification of a published directory entry. Kind is derived from filesystem attributes at enumeration time
/// and may be stale after the batch is published.
/// </summary>
public enum EntryKind
{
    File = 0,
    Directory = 1,
}
