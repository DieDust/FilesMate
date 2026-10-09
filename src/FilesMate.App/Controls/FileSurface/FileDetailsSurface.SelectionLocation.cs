namespace FilesMate.App.Controls.FileSurface;

public readonly record struct SelectedLocation(string Path, bool IsFile, int Count, bool CrossDirectories);

public sealed partial class FileDetailsSurface
{
    private long _locationSelectionRevision = -1;
    private WeakReference<FilesMate.Core.Recursive.RecursiveProjection>? _locationProjection;
    private SelectedLocation? _selectedLocation;

    public SelectedLocation? GetSelectedLocation()
    {
        var projection = _items.Binding?.Recursive;
        if (projection is null || _selection.Count == 0) { _selectedLocation = null; _locationProjection = null; return null; }
        if (_locationProjection?.TryGetTarget(out var previous) == true && ReferenceEquals(projection, previous) && _locationSelectionRevision == _selection.Revision)
            return _selectedLocation;
        _locationProjection = new(projection);
        _locationSelectionRevision = _selection.Revision;
        int? lead = _selection.SelectedLeadId;
        if (lead is int explicitId && (!_selection.Contains(explicitId) || !projection.IsSelectable(explicitId))) lead = null;
        var useVisibleFallback = lead is null;
        var firstView = int.MaxValue;
        int? parent = null;
        var count = 0;
        var cross = false;
        foreach (var id in _selection.Ids)
        {
            if (!projection.IsSelectable(id)) continue;
            ++count;
            var node = projection.GetNode(id);
            if (parent is int firstParent && firstParent != node.ParentId) cross = true;
            parent ??= node.ParentId;
            if (useVisibleFallback)
            {
                var view = projection.ViewIndexOfId(id);
                if (view < firstView) { firstView = view; lead = id; }
            }
        }
        return _selectedLocation = lead is int selected && count > 0
            ? new(projection.ResolvePath(selected), !projection.GetNode(selected).IsDirectory, count, cross) : null;
    }
}
