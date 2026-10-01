using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Locks;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Dispatching;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileTile
{
    private CancellationTokenSource? _tileSizeRequest;
    private CancellationToken _tileSizeOwner;
    private bool _tileRetainKnownSize;
    private long _tileSizeVersion;
    private Task? _tileSizeTask;

    private bool CanMeasureTileFolder() => _path is { Length: > 0 } path
        && !PortableDeviceLocation.TryParse(path, out _)
        && (Entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) == 0;

    private void UpdateTileFolderSize()
    {
        var path = _path!;
        if (FolderSizeCache.TryGet(path, out var bytes)
            || _tileRetainKnownSize && FolderSizeCache.TryGetUnchanged(path, out bytes))
        {
            SizeText.Text = DisplayTileBytes(bytes);
            return;
        }
        if (_tileSizeRequest?.IsCancellationRequested == false && _tileSizeTask is { IsCompleted: false }) return;
        CancelTileFolderSize();
        var hadKnown = FolderSizeCache.TryGetLastKnown(path, out var previous);
        SizeText.Text = hadKnown ? DisplayTileBytes(previous) : "…";
        _tileSizeRequest = CancellationTokenSource.CreateLinkedTokenSource(_tileSizeOwner);
        _tileSizeTask = FillTileFolderSizeAsync(path, _tileSizeVersion, _tileSizeRequest.Token, hadKnown);
    }

    private async Task FillTileFolderSizeAsync(string path, long version, CancellationToken token, bool hadKnown)
    {
        try
        {
            var bytes = await FolderSizeCache.GetAsync(path, token, hadKnown ? null : Report).ConfigureAwait(false);
            Report(bytes);
        }
        catch (OperationCanceledException)
        {
            // Cache invalidation cancels an old walk; visible owners request the new result.
            if (!token.IsCancellationRequested)
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (version != _tileSizeVersion || !IsLoaded || _tileSizeOwner.IsCancellationRequested) return;
                    CancelTileFolderSize();
                    UpdateSizeText();
                });
        }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Folder tile size: {0}", error.Message); }

        void Report(ulong bytes) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!token.IsCancellationRequested && version == _tileSizeVersion)
                SizeText.Text = DisplayTileBytes(bytes);
        });
    }

    private static string DisplayTileBytes(ulong bytes) =>
        DriveCapacity.FormatBytes(bytes > long.MaxValue ? long.MaxValue : (long)bytes);

    private void CancelTileFolderSize()
    {
        ++_tileSizeVersion;
        _tileSizeRequest?.Cancel();
        _tileSizeRequest?.Dispose();
        _tileSizeRequest = null;
    }

    internal void CancelFolderSizeWithin(IReadOnlyList<string> paths)
    {
        if (_path is { } path && paths.Any(root => FileLockPath.Matches(path, root, directory: true)))
            CancelTileFolderSize();
    }
}
