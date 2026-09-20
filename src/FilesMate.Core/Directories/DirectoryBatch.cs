using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;

namespace FilesMate.Core.Directories;

/// <summary>
/// One published batch of entries for a pane generation. The UI must ignore a batch when pane or generation mismatch.
/// Thread-safety: immutable after <see cref="Create"/>. Ownership: the list is snapshotted; callers must not mutate it.
/// Cancellation: a cancelled enumerator must not publish further batches.
/// Errors: <see cref="Error"/> may be terminal or partial; entries in the same batch remain readable.
/// Staleness: names and attributes are a snapshot of the filesystem at enumeration time.
/// </summary>
public sealed record DirectoryBatch
{
    private DirectoryBatch(
        PaneId paneId,
        long generation,
        string directoryPath,
        IReadOnlyList<FileEntryCore> entries,
        bool isFinal,
        DirectoryReadError? error)
    {
        PaneId = paneId;
        Generation = generation;
        DirectoryPath = directoryPath;
        Entries = entries;
        IsFinal = isFinal;
        Error = error;
    }

    public PaneId PaneId { get; }

    public long Generation { get; }

    public string DirectoryPath { get; }

    public IReadOnlyList<FileEntryCore> Entries { get; }

    public bool IsFinal { get; }

    public DirectoryReadError? Error { get; }

    public static DirectoryBatch Create(
        PaneId paneId,
        long generation,
        string directoryPath,
        IReadOnlyList<FileEntryCore> entries,
        bool isFinal,
        DirectoryReadError? error)
    {
        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation));
        }

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path is empty.", nameof(directoryPath));
        }

        ArgumentNullException.ThrowIfNull(entries);
        FileEntryCore[] snapshot = [.. entries];
        return new DirectoryBatch(paneId, generation, directoryPath, snapshot, isFinal, error);
    }

    public DirectoryBatch Concat(DirectoryBatch other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.PaneId != PaneId || other.Generation != Generation ||
            !string.Equals(other.DirectoryPath, DirectoryPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot mix pane, generation, or path identity across directory batches.");
        }

        var combined = new FileEntryCore[Entries.Count + other.Entries.Count];
        for (var i = 0; i < Entries.Count; i++)
        {
            combined[i] = Entries[i];
        }

        for (var i = 0; i < other.Entries.Count; i++)
        {
            combined[Entries.Count + i] = other.Entries[i];
        }

        var error = other.Error ?? Error;
        return new DirectoryBatch(PaneId, Generation, DirectoryPath, combined, other.IsFinal || IsFinal, error);
    }
}
