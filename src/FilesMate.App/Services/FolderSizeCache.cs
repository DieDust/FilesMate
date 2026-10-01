using FilesMate.Platform.Windows.Directories;

namespace FilesMate.App.Services;

/// <summary>One bounded, cancellable size scheduler shared by visible rows.</summary>
public static class FolderSizeCache
{
    private static readonly FolderSizeService Service = new(FolderSizeWalker.Measure);

    public static event Action<string>? SizeCached
    {
        add => Service.SizeCached += value;
        remove => Service.SizeCached -= value;
    }

    public static bool TryGet(string path, out ulong size) => Service.TryGet(path, out size);

    public static Task<ulong> GetAsync(string path, CancellationToken cancellationToken = default,
        Action<ulong>? progress = null) => Service.GetAsync(path, cancellationToken, progress);

    public static void Invalidate(string path) => Service.Invalidate(path);

    public static bool TryGetLastKnown(string path, out ulong size) => Service.TryGetLastKnown(path, out size);

    public static bool TryGetUnchanged(string path, out ulong size) => Service.TryGetUnchanged(path, out size);

    public static void MarkChanged(string path, bool includeDescendants = false) => Service.MarkChanged(path, includeDescendants);

    public static async Task<ulong> GetTotalAsync(string path, CancellationToken token, bool useUnchanged)
    {
        // Read only immediate children; reuse their walks instead of crawling every
        // sibling again just to refresh the status-bar total. Include hidden items.
        var request = new FilesMate.Core.Directories.DirectoryRequest(default, 0, path,
            FilesMate.Core.Directories.DirectoryReadOptions.Default with { IncludeHidden = true, IncludeSystem = true });
        ulong total = 0;
        await foreach (var batch in new WindowsDirectoryEnumerator().EnumerateAsync(request, token).ConfigureAwait(false))
        {
            if (batch.Error is { } error) throw new IOException(error.Message);
            foreach (var entry in batch.Entries)
            {
                token.ThrowIfCancellationRequested();
                if ((entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) continue;
                var bytes = entry.Kind == FilesMate.Core.Entries.EntryKind.Directory
                    ? await Service.GetAsync(Path.Combine(path, entry.Name), token, useUnchanged: useUnchanged).ConfigureAwait(false)
                    : entry.Size;
                total = ulong.MaxValue - total < bytes ? ulong.MaxValue : total + bytes;
            }
        }
        return total;
    }
}
