namespace FilesMate.Core.Operations;

public enum FileUndoKind
{
    Created,
    Relocated,
    Recycled,
    Grouped,
    Merged,
    Copied,
}

public readonly record struct FilePathPair(string Source, string Destination)
{
    public FilePathPair Reverse() => new(Destination, Source);
}

public sealed record FileUndoRecord(
    FileUndoKind Kind,
    IReadOnlyList<string> Paths,
    IReadOnlyList<FilePathPair> Pairs)
{
    private FileUndoState _undoState = FileUndoState.Capture(
        Kind is FileUndoKind.Relocated or FileUndoKind.Merged or FileUndoKind.Grouped
            ? Pairs.Select(pair => pair.Destination)
            : Kind is FileUndoKind.Created or FileUndoKind.Copied ? Paths : []);
    private FileUndoState? _redoState;

    internal void ValidateUndo(ILocalFileOperations operations)
    {
        if (operations.RequiresUndoValidation) _undoState.Validate();
    }
    internal void ValidateRedo(ILocalFileOperations operations)
    {
        if (operations.RequiresUndoValidation) (_redoState ?? throw new IOException("Missing undo state.")).Validate();
    }
    internal void CaptureRedo() => _redoState = FileUndoState.Capture(
        Kind is FileUndoKind.Relocated or FileUndoKind.Merged or FileUndoKind.Grouped
            ? Pairs.Select(pair => pair.Source) : Kind == FileUndoKind.Recycled ? Paths : []);
    internal void CaptureUndo() => _undoState = FileUndoState.Capture(
        Kind is FileUndoKind.Relocated or FileUndoKind.Merged or FileUndoKind.Grouped
            ? Pairs.Select(pair => pair.Destination)
            : Kind is FileUndoKind.Created or FileUndoKind.Copied ? Paths : []);

    internal void AcceptReplacementState(FileReplacement replacement)
    {
        // These snapshots were captured by our successful swaps. Preserve all unrelated
        // original snapshots so a partial retry never accepts an external modification.
        _undoState = _undoState.WithReplacement(replacement.DestinationState);
    }

    internal bool HasOrdinaryActions => Paths.Count > 0 || Pairs.Count > 0 || CreatedDirectories.Count > 0;
    internal bool HasActions => HasOrdinaryActions || Replacements.Count > 0;

    public IReadOnlyList<string> CreatedDirectories { get; init; } = [];
    public IReadOnlyList<FileReplacement> Replacements { get; init; } = [];
    public IReadOnlyList<RecycleItemResult> RecycledItems { get; internal set; } = [];

    internal FileUndoRecord SelectPaths(IReadOnlyList<string> paths, IReadOnlyList<FileReplacement> replacements)
    {
        var selected = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return this with
        {
            Paths = paths.ToArray(), Replacements = replacements,
            RecycledItems = RecycledItems.Where(item => selected.Contains(item.OriginalPath)).ToArray(),
            _undoState = _undoState.SelectRoots(paths), _redoState = _redoState?.SelectRoots(paths),
        };
    }

    public static FileUndoRecord Copied(IReadOnlyList<string> files, IReadOnlyList<string> directories) =>
        new(FileUndoKind.Copied, files.Count == 0 ? [] : Snapshot(files), [])
        { CreatedDirectories = directories.Count == 0 ? [] : Snapshot(directories) };

    public static FileUndoRecord Created(IReadOnlyList<string> paths) =>
        new(FileUndoKind.Created, Snapshot(paths), []);

    public static FileUndoRecord Recycled(IReadOnlyList<string> paths) =>
        new(FileUndoKind.Recycled, Snapshot(paths), []);

    public static FileUndoRecord RecycledWithReceipts(IReadOnlyList<RecycleItemResult> items) =>
        new(FileUndoKind.Recycled, Snapshot(items.Select(item => item.OriginalPath).ToArray()), [])
        { RecycledItems = items.ToArray() };

    public static FileUndoRecord Relocated(IReadOnlyList<FilePathPair> pairs) =>
        new(FileUndoKind.Relocated, [], Snapshot(pairs));
    public static FileUndoRecord Grouped(string folder, IReadOnlyList<FilePathPair> pairs) =>
        new(FileUndoKind.Grouped, Snapshot([folder]), Snapshot(pairs));

    public static FileUndoRecord Merged(IReadOnlyList<string> removedDirectories, IReadOnlyList<FilePathPair> pairs) =>
        new(FileUndoKind.Merged, removedDirectories.Count == 0 ? [] : Snapshot(removedDirectories),
            pairs.Count == 0 ? [] : Snapshot(pairs));

    private static IReadOnlyList<string> Snapshot(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("At least one path is required.", nameof(paths));
        }

        var copy = new string[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(paths[i]))
            {
                throw new ArgumentException("Paths must be non-empty.", nameof(paths));
            }

            copy[i] = paths[i];
        }

        return copy;
    }

    private static IReadOnlyList<FilePathPair> Snapshot(IReadOnlyList<FilePathPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
        {
            throw new ArgumentException("At least one path pair is required.", nameof(pairs));
        }

        var copy = new FilePathPair[pairs.Count];
        for (var i = 0; i < pairs.Count; i++)
        {
            var pair = pairs[i];
            if (string.IsNullOrWhiteSpace(pair.Source) || string.IsNullOrWhiteSpace(pair.Destination))
            {
                throw new ArgumentException("Path pairs must be non-empty.", nameof(pairs));
            }

            copy[i] = pair;
        }

        return copy;
    }
}
