using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;

namespace FilesMate.App.Navigation;

/// <summary>
/// One published view and its path context. A surface may retain this tuple while
/// navigation changes; its resolver must not consult the pane or a live session.
/// </summary>
public sealed class EntryViewBinding
{
    public EntryViewBinding(EntryStore store, EntryViewIndex index, long generation,
        string folderPath, Func<FileEntryCore, string> resolvePath, RecursiveProjection? recursive = null, Func<bool>? recursiveOperationsCurrent = null, long diagnosticOperation = 0,
        EntrySourcePrefixProof? sourcePrefixProof = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentNullException.ThrowIfNull(resolvePath);
        if (!index.Snapshot.BelongsTo(store))
            throw new ArgumentException("Index belongs to another store.", nameof(index));
        if (index.Generation != generation)
            throw new ArgumentException("Index belongs to another generation.", nameof(generation));
        Store = store;
        Index = index;
        Generation = generation;
        FolderPath = folderPath;
        ResolvePath = resolvePath;
        ResolveFolder = () => folderPath;
        Recursive = recursive;
        RecursiveOperationsCurrent = recursiveOperationsCurrent;
        DiagnosticOperation = diagnosticOperation;
        SourcePrefixProof = sourcePrefixProof;
    }

    public long DiagnosticOperation { get; }
    public RecursiveIdentityRebase? RecursiveRebase { get; init; }
    public EntrySourcePrefixProof? SourcePrefixProof { get; }
    public RecursiveProjection? Recursive { get; }
    public Func<bool>? RecursiveOperationsCurrent { get; }
    public EntryStore Store { get; }
    public EntryViewIndex Index { get; }
    public long Generation { get; }
    public string FolderPath { get; }
    public Func<FileEntryCore, string> ResolvePath { get; }
    public Func<string?> ResolveFolder { get; }

    /// <summary>Validates a recycled presenter's exact row and captured path.</summary>
    public bool MatchesEntry(int viewIndex, int entryId, string path)
    {
        if ((uint)viewIndex >= (uint)Index.Count) return false;
        var entry = Index.GetEntry(Store, viewIndex);
        return entry.Id == entryId && string.Equals(ResolvePath(entry), path, StringComparison.OrdinalIgnoreCase);
    }
}
