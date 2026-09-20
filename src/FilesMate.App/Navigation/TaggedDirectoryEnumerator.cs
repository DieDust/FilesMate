using System.IO;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Navigation;

public sealed class TaggedDirectoryEnumerator : IDirectoryEnumerator
{
    private readonly IDirectoryEnumerator _inner;
    private readonly Func<long, CancellationToken, Task<IReadOnlyList<string>>> _pathsForTag;

    public TaggedDirectoryEnumerator(
        IDirectoryEnumerator inner,
        Func<long, CancellationToken, Task<IReadOnlyList<string>>> pathsForTag)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(pathsForTag);
        _inner = inner;
        _pathsForTag = pathsForTag;
    }

    public IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
        DirectoryRequest request,
        CancellationToken cancellationToken)
    {
        if (!TagLocation.TryParse(request.Path, out var tagId))
        {
            return _inner.EnumerateAsync(request, cancellationToken);
        }

        return EnumerateTaggedAsync(request, tagId, cancellationToken);
    }

    private async IAsyncEnumerable<DirectoryBatch> EnumerateTaggedAsync(
        DirectoryRequest request,
        long tagId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IReadOnlyList<string> paths = [];
        DirectoryReadError? error = null;
        try
        {
            paths = await _pathsForTag(tagId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = new DirectoryReadError(DirectoryReadErrorKind.Unknown, 0, ex.Message, isTerminal: true);
        }

        if (error is not null)
        {
            yield return DirectoryBatch.Create(
                request.PaneId,
                request.Generation,
                request.Path,
                [],
                isFinal: true,
                error);
            yield break;
        }

        var entries = new List<FileEntryCore>(paths.Count);
        var id = 1;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryEntry(path, id, out var entry))
            {
                entries.Add(entry);
                id++;
            }
        }

        yield return DirectoryBatch.Create(
            request.PaneId,
            request.Generation,
            request.Path,
            entries,
            isFinal: true,
            null);
    }

    public static bool TryEntry(string path, int id, out FileEntryCore entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            var directory = (attributes & FileAttributes.Directory) != 0;
            if (directory)
            {
                var info = new DirectoryInfo(path);
                entry = new FileEntryCore(
                    id,
                    path,
                    0,
                    info.LastWriteTimeUtc.Ticks,
                    info.CreationTimeUtc.Ticks,
                    attributes,
                    EntryKind.Directory) { AccessedUtcTicks = info.LastAccessTimeUtc.Ticks };
                return true;
            }

            var file = new FileInfo(path);
            entry = new FileEntryCore(
                id,
                path,
                (ulong)Math.Max(0, file.Length),
                file.LastWriteTimeUtc.Ticks,
                file.CreationTimeUtc.Ticks,
                attributes,
                EntryKind.File) { AccessedUtcTicks = file.LastAccessTimeUtc.Ticks };
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
