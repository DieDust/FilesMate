using FilesMate.Core.Entries;

namespace FilesMate.App.Services;

public readonly record struct SearchMetadataRequest(string Path, FileEntryCore Entry);

/// <summary>Three filesystem reads across windows; never called from row binding or scrolling.</summary>
public static class SearchResultMetadataLoader
{
    private static readonly SemaphoreSlim Workers = new(3, 3);

    public static async Task<FileEntryCore[]> ReadAsync(IReadOnlyList<SearchMetadataRequest> requests, CancellationToken token)
    {
        var results = requests.Select(request => request.Entry).ToArray();
        await Parallel.ForEachAsync(Enumerable.Range(0, requests.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, async (i, cancellation) =>
            {
                await Workers.WaitAsync(cancellation).ConfigureAwait(false);
                try { results[i] = await Task.Run(() => Read(requests[i], cancellation), cancellation).ConfigureAwait(false); }
                finally { Workers.Release(); }
            }).ConfigureAwait(false);
        return results;
    }

    private static FileEntryCore Read(SearchMetadataRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            FileSystemInfo info = request.Entry.Kind == EntryKind.Directory ? new DirectoryInfo(request.Path) : new FileInfo(request.Path);
            info.Refresh();
            if (!info.Exists) return request.Entry;
            var attributes = info.Attributes;
            var directory = (attributes & FileAttributes.Directory) != 0;
            var result = request.Entry with
            {
                CreatedUtcTicks = info.CreationTimeUtc.Ticks,
                ModifiedUtcTicks = info.LastWriteTimeUtc.Ticks,
                AccessedUtcTicks = info.LastAccessTimeUtc.Ticks,
                Attributes = attributes,
                Kind = directory ? EntryKind.Directory : EntryKind.File,
                Size = !directory && info is FileInfo file ? (ulong)file.Length : request.Entry.Size,
            };
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return request.Entry; } // Preserve known indexed values; unknown dates remain unknown.
    }
}
