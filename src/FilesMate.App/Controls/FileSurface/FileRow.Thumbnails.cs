using FilesMate.App.Icons;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileRow
{
    private ThumbnailLoadMode _thumbnailLoadMode = ThumbnailLoadMode.Active;

    internal bool HasPendingThumbnail => ShellIconBinder.HasPendingLoad(IconImage);

    internal long ThumbnailContentEpoch { get; private set; }

    internal void BindViewport(long owner, long contentEpoch, ThumbnailLoadMode mode, int viewIndex, in FileEntryCore entry,
        bool selected, string? path, CancellationToken cancellationToken, bool retainKnownSize)
    {
        ThumbnailContentEpoch = contentEpoch;
        _thumbnailLoadMode = mode;
        Bind(viewIndex, entry, selected, path, cancellationToken, retainKnownSize);
        UpdateThumbnailDemand(mode, owner);
    }

    internal void UpdateThumbnailDemand(ThumbnailLoadMode mode, long owner, bool retainCompleted = false, bool settled = false)
    {
        var changed = _thumbnailLoadMode != mode;
        _thumbnailLoadMode = mode;
        ShellIconBinder.SetLoadMode(IconImage, Glyph, mode, owner, retainCompleted, settled);
        // Bind already refreshes metadata for a new identity. A viewport shape
        // change that keeps this row active must not rebuild its column array
        // and signature (or repeatedly cancel an already-suspended request).
        if (!changed) return;
        if (mode == ThumbnailLoadMode.Active)
        {
            RefreshProperties();
            if (!_recursiveRow) RequestFolderSize(_entryPath, _sizeOwnerToken, retainKnownSize: true);
        }
        else
        {
            CancelPropertyRead();
            // DeferredVisible keeps an already-running visible walk, but does
            // not start one. Suspension retires the row's own directory walk.
            if (mode == ThumbnailLoadMode.Suspended) CancelFolderSize();
        }
    }
}
