using FilesMate.App.Icons;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileTile
{
    private ThumbnailLoadMode _thumbnailLoadMode = ThumbnailLoadMode.Active;

    internal bool HasPendingThumbnail => ShellIconBinder.HasPendingLoad(IconImage)
        || FolderPreviewBinder.HasPendingLoad(FolderPreviewHost);

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
        FolderPreviewBinder.SetLoadMode(FolderPreviewHost, FolderPreviewCover, mode);
        if (!changed) return;
        if (mode == ThumbnailLoadMode.Active) UpdateSizeText();
        else if (mode == ThumbnailLoadMode.Suspended) CancelTileFolderSize();
    }
}
