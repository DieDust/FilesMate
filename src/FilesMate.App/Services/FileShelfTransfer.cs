using FilesMate.Core.Operations;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Services;

public sealed record ShelfTransferResult(IReadOnlyList<FilePathPair> Completed, IReadOnlyList<string> Errors, bool Cancelled)
{
    public int Skipped { get; init; }
    public FileUndoRecord? Undo { get; init; }
    public int WithoutUndo { get; init; }
}

public static class FileShelfTransfer
{
    public static async Task<ShelfTransferResult> RunAsync(
        ILocalFileOperations operations, IReadOnlyList<string> sources, string destination, bool move,
        IProgress<int>? progress = null, CancellationToken token = default,
        bool allowSameDirectoryCopy = false, FileConflictResolver? resolveConflict = null)
    {
        using var lifetime = FileOperationLifetime.Begin();
        var errors = new List<string>();
        var requests = new List<FilePathPair>();
        var unchanged = 0;
        var roots = SelectRoots(sources);
        foreach (var source in roots)
        {
            if (token.IsCancellationRequested) break;
            if (move && string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source))),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase))
            { unchanged++; continue; }
            if (FileDropPolicy.FilterSources([source], destination, move, allowSameDirectoryCopy).Count == 0)
                errors.Add($"{source}: {StringTable.Get("Transfer_InvalidDestination")}");
            else requests.Add(new(source, Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)))));
        }
        var result = await WindowsFileTransfer.RunAsync(operations, requests, move, resolveConflict, progress, token).ConfigureAwait(false);
        return new(result.Completed, errors.Concat(result.Errors).ToArray(), result.Cancelled)
        { Skipped = result.Skipped + unchanged, Undo = result.Undo, WithoutUndo = result.WithoutUndo };
    }

    internal static IReadOnlyList<string> SelectRoots(IReadOnlyList<string> sources)
    {
        var distinct = sources.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selected = distinct.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>(distinct.Length);
        foreach (var source in distinct)
        {
            var parent = Path.GetDirectoryName(source);
            while (parent is not null && !selected.Contains(parent)) parent = Path.GetDirectoryName(parent);
            if (parent is null) roots.Add(source);
        }
        return roots;
    }
}
