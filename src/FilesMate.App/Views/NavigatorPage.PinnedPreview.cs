using System.IO;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    public event Action<string?>? PinnedPreviewChanged;
    public event Action<bool>? PinnedPreviewVisibilityChanged;

    private string? _pinnedPreviewPath;
    private FileSystemWatcher? _pinnedPreviewWatcher;
    private CancellationTokenSource? _pinnedPreviewRefresh;

    private void TogglePinnedPreview()
    {
        if (_pinnedPreviewPath is not null)
        {
            ResetPinnedPreview();
            _ = LoadSelectedPreviewAsync();
            return;
        }

        var path = _loadedPreviewPath;
        if (!_previewVisible || string.IsNullOrWhiteSpace(path)) return;
        _pinnedPreviewPath = path;
        PreviewHost.SetPinState(true, path);
        StartPinnedPreviewWatcher(path);
        PinnedPreviewChanged?.Invoke(path);
    }

    public void ApplyPinnedPreviewFromWindow(string? path, bool visible)
    {
        if (_disposed) return;
        var pathChanged = !string.Equals(_pinnedPreviewPath, path, StringComparison.OrdinalIgnoreCase);
        if (pathChanged)
        {
            StopPinnedPreviewWatcher();
            _pinnedPreviewPath = path;
            _loadedPreviewPath = null;
            _previewHost?.CancelAndClear();
        }
        if (path is null)
        {
            _previewHost?.SetPinState(true, null);
            if (pathChanged && _previewVisible && IsLoaded) _ = LoadSelectedPreviewAsync();
            return;
        }

        var visibilityChanged = _previewVisible != visible;
        if (visibilityChanged) SetPreviewVisible(visible);
        _previewHost?.SetPinState(true, path);
        if (pathChanged && !visibilityChanged && visible && IsLoaded)
        {
            StartPinnedPreviewWatcher(path);
            _ = RefreshPinnedPreviewAsync(path);
        }
    }

    private void ResetPinnedPreview(bool notifyWindow = true)
    {
        var wasPinned = _pinnedPreviewPath is not null;
        StopPinnedPreviewWatcher();
        _pinnedPreviewPath = null;
        _loadedPreviewPath = null;
        _previewHost?.CancelAndClear();
        _previewHost?.SetPinState(true, null);
        if (wasPinned && notifyWindow) PinnedPreviewChanged?.Invoke(null);
    }

    private bool PinnedPreviewUsesAny(IReadOnlyList<string> roots)
    {
        if (_pinnedPreviewPath is not { } pinned) return true;
        foreach (var path in roots)
        {
            var root = path.TrimEnd('\\', '/');
            if (pinned.Equals(root, StringComparison.OrdinalIgnoreCase)
                || pinned.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase)
                || pinned.StartsWith(root + '/', StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void StartPinnedPreviewWatcher(string path)
    {
        StopPinnedPreviewWatcher();
        FileSystemWatcher? watcher = null;
        try
        {
            // A portable device or network location may not support directory
            // notifications. The refresh button remains available for those paths.
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return;
            var directory = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name) || !Directory.Exists(directory)) return;

            watcher = new FileSystemWatcher(directory, name)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watcher.Changed += (_, _) => QueuePinnedPreviewRefresh(path);
            watcher.Created += (_, _) => QueuePinnedPreviewRefresh(path);
            watcher.Deleted += (_, _) => QueuePinnedPreviewRefresh(path);
            watcher.Renamed += (_, _) => QueuePinnedPreviewRefresh(path);
            watcher.Error += (_, _) => DispatcherQueue.TryEnqueue(() => StopPinnedPreviewWatcher());
            watcher.EnableRaisingEvents = true;
            _pinnedPreviewWatcher = watcher;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            watcher?.Dispose();
            // Pinning remains usable; the user can refresh a location that cannot be watched.
        }
    }

    private void StopPinnedPreviewWatcher()
    {
        _pinnedPreviewWatcher?.Dispose();
        _pinnedPreviewWatcher = null;
        _pinnedPreviewRefresh?.Cancel();
        _pinnedPreviewRefresh?.Dispose();
        _pinnedPreviewRefresh = null;
    }

    private void QueuePinnedPreviewRefresh(string path)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !_previewVisible || !IsLoaded ||
                !string.Equals(_pinnedPreviewPath, path, StringComparison.OrdinalIgnoreCase)) return;
            _pinnedPreviewRefresh?.Cancel();
            _pinnedPreviewRefresh?.Dispose();
            var refresh = new CancellationTokenSource();
            _pinnedPreviewRefresh = refresh;
            _ = RefreshPinnedPreviewAfterChangeAsync(path, refresh.Token);
        });
    }

    private async Task RefreshPinnedPreviewAfterChangeAsync(string path, CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token).ConfigureAwait(false);
            await RefreshPinnedPreviewAsync(path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshPinnedPreviewAsync(string? requestedPath = null, CancellationToken token = default)
    {
        var path = requestedPath ?? _pinnedPreviewPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        var exists = await Task.Run(() => File.Exists(path) || Directory.Exists(path), token).ConfigureAwait(false);
        if (token.IsCancellationRequested) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed || !_previewVisible || !IsLoaded ||
                !string.Equals(_pinnedPreviewPath, path, StringComparison.OrdinalIgnoreCase)) return;
            _loadedPreviewPath = null;
            if (exists) _ = LoadSelectedPreviewAsync();
            else PreviewHost.ShowPinnedUnavailable();
        });
    }
}
