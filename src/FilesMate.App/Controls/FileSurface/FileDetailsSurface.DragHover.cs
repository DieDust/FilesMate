using Microsoft.UI.Dispatching;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private DispatcherQueueTimer? _folderHoverTimer;
    private string? _folderHoverPath;

    private void UpdateFolderHover(string? path)
    {
        if (string.Equals(path, _folderHoverPath, StringComparison.OrdinalIgnoreCase)) return;
        CancelFolderHover();
        if (path is null) return;
        _folderHoverPath = path;
        _folderHoverTimer ??= CreateFolderHoverTimer();
        _folderHoverTimer.Start();
    }

    private DispatcherQueueTimer CreateFolderHoverTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(750);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            var path = _folderHoverPath;
            if (!IsLoaded || path is null || !string.Equals(path, ResolveDropTargetDirectory(), StringComparison.OrdinalIgnoreCase)) return;
            if (_items.TryGetEntry(_dropTargetViewIndex, out var entry) && entry.Kind == EntryKind.Directory)
            {
                CancelFolderHover();
                OpenRequested?.Invoke(this, entry);
            }
        };
        return timer;
    }

    private void CancelFolderHover()
    {
        _folderHoverTimer?.Stop();
        _folderHoverPath = null;
    }
}
