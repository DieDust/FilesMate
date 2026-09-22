using FilesMate.Core.Operations;
using FilesMate.App.Localization;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Services;

public sealed record ShelfTransferResult(IReadOnlyList<FilePathPair> Completed, IReadOnlyList<string> Errors, bool Cancelled)
{
    public int Skipped { get; init; }
    public FileUndoRecord? Undo { get; init; }
    public int WithoutUndo { get; init; }
    public IReadOnlyList<string> RemovedSourceDirectories { get; init; } = [];
}

public static class FileShelfTransfer
{
    public static async Task<ShelfTransferResult> RunAsync(
        ILocalFileOperations operations, IReadOnlyList<string> sources, string destination, bool move,
        IProgress<int>? progress = null, CancellationToken token = default,
        bool allowSameDirectoryCopy = false, FileConflictResolver? resolveConflict = null, IProgress<FileCopyProgress>? byteProgress = null)
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
        var result = await WindowsFileTransfer.RunAsync(operations, requests, move, resolveConflict, progress, token, byteProgress: byteProgress).ConfigureAwait(false);
        return new(result.Completed, errors.Concat(result.Errors).ToArray(), result.Cancelled)
        { Skipped = result.Skipped + unchanged, Undo = result.Undo, WithoutUndo = result.WithoutUndo,
            RemovedSourceDirectories = result.RemovedSourceDirectories };
    }

    public static async Task RemoveMovedSourcesAsync(FileShelfStore shelf, ShelfTransferResult result)
    {
        var entries = await shelf.GetAsync().ConfigureAwait(false);
        if (entries.Count == 0 || result.Completed.Count + result.RemovedSourceDirectories.Count == 0) return;
        // Only inspect paths covered by completed work. A cancelled/failed source that
        // disappeared externally must remain in the shelf, and unrelated network paths
        // must not add disk latency to completing a local transfer.
        var missing = await Task.Run(() =>
        {
            var completed = result.Completed.Select(pair => pair.Source).Concat(result.RemovedSourceDirectories)
                .Select(Path.TrimEndingDirectorySeparator).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return entries.Where(path =>
            {
                for (var current = Path.TrimEndingDirectorySeparator(path); current is not null; current = Path.GetDirectoryName(current))
                    if (completed.Contains(current)) return ConfirmedMissing(path);
                return false;
            }).ToArray();
        }).ConfigureAwait(false);
        if (missing.Length > 0) await shelf.RemoveAsync(missing).ConfigureAwait(false);
    }

    private static bool ConfirmedMissing(string path)
    {
        if (!Directory.Exists(Path.GetPathRoot(path))) return false;
        try { File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
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
