using FilesMate.Core.Directories;
using FilesMate.App.Controls.Menus;
using FilesMate.App.Models;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using FilesMate.App.Localization;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private static int RecursiveAltExpansionDepth => App.ExplorerPreferences.RecursiveAltExpandDepth == int.MaxValue
        ? int.MaxValue : Math.Clamp(App.ExplorerPreferences.RecursiveAltExpandDepth, 1, 5);

    private bool CanOwnRecursiveAltActivation(FileEntryCore entry) => _recursiveMode && entry.Kind == EntryKind.Directory
        && _items.Binding?.Recursive is { IsFiltering: false } projection && projection.IsVisible(entry.Id)
        && !projection.GetNode(entry.Id).TraversalSkipped
        && !_resourcesReleased && _items.Binding.Generation == _generation;

    private void ActivateEntry(FileEntryCore entry, bool altRecursiveExpansion = false)
    {
        if (_recursiveMode && entry.Kind == EntryKind.Directory)
        {
            if (_items.Binding?.Recursive is not { } projection) return;
            var node = projection.GetNode(entry.Id);
            if (projection.GetChildState(entry.Id, _items.Binding?.RecursiveOperationsCurrent?.Invoke() == true) == RecursiveChildState.Empty) return;
            if (node.TraversalSkipped)
            {
                RecursiveOperationBlocked?.Invoke(this, StringTable.Get("Recursive_NotTraversed"));
                return;
            }
            // Pointer recognition already uses the ordinary per-kind opening
            // preference. Enter shares its directory action; the arrow is a
            // separate button excluded by IsSelectionToggleSource.
            // The existing projection owner retains its filtering policy.
            if (altRecursiveExpansion)
            {
                if (CanOwnRecursiveAltActivation(entry))
                    RecursiveAltDepthRequested?.Invoke(this, (entry.Id, RecursiveAltExpansionDepth));
            }
            else if (RecursiveOpenRequested is not null) RecursiveOpenRequested.Invoke(this, entry.Id);
            else RecursiveToggleRequested?.Invoke(this, entry.Id);
            return;
        }
        OpenRequested?.Invoke(this, entry);
    }

    private bool _recursiveMode;
    private bool _dragRecursiveSource;
    private long _recursiveLastInput;
    private TimeSpan _recursiveInputQuietPeriod;
    internal bool DeferRecursiveProgress => IsMemoryReclamationBusy || _scrollbarDragging || _recursiveMode && _recursiveLastInput != 0
        && System.Diagnostics.Stopwatch.GetElapsedTime(_recursiveLastInput) < _recursiveInputQuietPeriod;
    private FilesMate.Core.Recursive.RecursiveProjection? _prunedRecursiveProjection;
    private long _prunedSelectionRevision = -1;
    private DetailsColumn[]? _ordinaryColumns;
    private void EnsureRecursiveNameWidth(RecursiveProjection? projection)
    {
        if (!_recursiveMode || projection is null) return;
        var width = Math.Max(_ordinaryColumns?.FirstOrDefault(c => c.Id == DetailsColumnId.Name)?.Width
            ?? FileColumnLayout.NameWidth,
            projection.MaximumVisibleDepth * FileColumnLayout.RecursiveIndentWidth + FileColumnLayout.RecursiveArrowWidth + FileColumnLayout.MinNameWidth);
        if (_detailColumns.First(c => c.Id == DetailsColumnId.Name).Width >= width) return;
        // This width is transient. ColumnsForPersistence retains the ordinary
        // layout, while the existing horizontal scroller exposes deep names.
        _detailColumns = _detailColumns.Select(c => c.Id == DetailsColumnId.Name ? c with { Width = width } : c).ToArray();
        ApplyDetailsColumns(persist: false);
    }
    private FileLayoutKind _ordinaryLayout;
    public FileLayoutKind LayoutForPersistence => _recursiveMode ? _ordinaryLayout : _layout;
    public DetailsColumn[] ColumnsForPersistence() => _recursiveMode && _ordinaryColumns is not null
        ? _ordinaryColumns.ToArray() : GetPresentationColumns();
    public event EventHandler<int>? RecursiveToggleRequested;
    public event EventHandler<int>? RecursiveOpenRequested;
    public event EventHandler<(int EntryId, int Depth)>? RecursiveAltDepthRequested;
    public event EventHandler<(IReadOnlyList<int> EntryIds, int Depth)>? RecursiveDepthRequested;
    public event EventHandler<(int EntryId, bool Expanded, bool Subtree)>? RecursiveExpansionRequested;
    public Func<int, bool>? RecursiveDesiredExpanded { get; set; }
    public Func<int, bool>? RecursiveDesiredSubtreeExpanded { get; set; }
    public Func<bool>? CancelRecursiveScanRequested { get; set; }
    public Func<bool>? RecursiveCanContinue { get; set; }
    public Action? RecursiveContinueRequested { get; set; }
    public event EventHandler<FileLayoutKind>? RecursiveLayoutRequested;

    public bool IsRecursiveView => _recursiveMode;
    public event EventHandler<string>? RecursiveOperationBlocked;

    public Func<bool> CaptureRecursiveOperationGuard()
    {
        if (!_recursiveMode) return () => true;
        var binding = _items.Binding;
        var ids = _selection.Ids.ToArray();
        return () => !_resourcesReleased && _recursiveMode && binding?.Recursive is { } projection
            && _items.Binding?.Generation == binding.Generation
            && binding.RecursiveOperationsCurrent?.Invoke() == true
            && projection.EvaluateOperationSafety(ids).IsSafe;
    }

    public bool ValidateRecursiveSelection(RecursiveOperationScope scope = RecursiveOperationScope.Subtree, bool report = true)
    {
        if (_items.Binding?.Recursive is not { } projection)
        {
            if (_recursiveMode && report) RecursiveOperationBlocked?.Invoke(this, StringTable.Get("Recursive_OperationUnavailable"));
            return !_recursiveMode;
        }
        var safe = _items.Binding.RecursiveOperationsCurrent?.Invoke() == true
            && projection.EvaluateOperationSafety(_selection.Ids, scope).IsSafe;
        if (!safe && report) RecursiveOperationBlocked?.Invoke(this, StringTable.Get("Recursive_OperationUnavailable"));
        return safe;
    }

    public bool ValidateRecursiveEntry(int entryId, RecursiveOperationScope scope = RecursiveOperationScope.Subtree, bool report = true)
    {
        if (_items.Binding?.Recursive is not { } projection)
        {
            if (_recursiveMode && report) RecursiveOperationBlocked?.Invoke(this, StringTable.Get("Recursive_OperationUnavailable"));
            return !_recursiveMode;
        }
        var safe = _items.Binding.RecursiveOperationsCurrent?.Invoke() == true && projection.IsOperationSafe(entryId, scope);
        if (!safe && report) RecursiveOperationBlocked?.Invoke(this, StringTable.Get("Recursive_OperationUnavailable"));
        return safe;
    }

    public bool ValidateRecursiveRoot(bool report = true)
    {
        if (_items.Binding?.Recursive is not { } projection) return !_recursiveMode;
        var safe = _items.Binding.RecursiveOperationsCurrent?.Invoke() == true
            && projection.Snapshot.RootListingComplete
            && projection.Snapshot.RootOperationCompatibility == RecursiveOperationCompatibility.Compatible;
        if (!safe && report) RecursiveOperationBlocked?.Invoke(this, StringTable.Get("Recursive_OperationUnavailable"));
        return safe;
    }

    public void SetRecursiveMode(bool enabled)
    {
        if (_recursiveMode == enabled) return;
        _recursiveMode = enabled;
        _prunedRecursiveProjection = null;
        _prunedSelectionRevision = -1;
        if (enabled)
        {
            _ordinaryColumns = GetPresentationColumns();
            _ordinaryLayout = LayoutKind;
            SetLayout(FileLayoutKind.Details);
            SetColumns(RecursiveColumnLayout.Create(_ordinaryColumns, Scroller.ActualWidth));
        }
        else
        {
            if (_ordinaryColumns is not null) SetColumns(_ordinaryColumns);
            SetLayout(_ordinaryLayout);
            _ordinaryColumns = null;
        }
        RebindVisibleEntries();
    }

    private void PruneRecursiveSelection()
    {
        if (_items.Binding?.Recursive is not { } projection)
        {
            _prunedRecursiveProjection = null;
            return;
        }
        if (ReferenceEquals(_prunedRecursiveProjection, projection) && _prunedSelectionRevision == _selection.Revision) return;
        _selection.RemoveWhere(id => !projection.IsSelectable(id));
        _prunedRecursiveProjection = projection;
        _prunedSelectionRevision = _selection.Revision;
    }

    private bool HandleRecursiveTreeKey(Windows.System.VirtualKey key, int current)
    {
        if (_items.Binding?.Recursive is not { } projection || projection.IsFiltering
            || !_items.TryGetEntry(current, out var entry)) return false;
        var node = projection.GetNode(entry.Id);
        if (key == Windows.System.VirtualKey.Right && node.IsDirectory && !node.TraversalSkipped)
        {
            var desired = RecursiveDesiredExpanded?.Invoke(entry.Id) ?? projection.IsExpanded(entry.Id);
            if (!desired) RecursiveExpansionRequested?.Invoke(this, (entry.Id, true, false));
            else if (projection.IsExpanded(entry.Id) && projection.HasChildren(entry.Id)) MoveTo(current + 1, false);
            return true;
        }
        if (key == Windows.System.VirtualKey.Left)
        {
            if (node.IsDirectory && !node.TraversalSkipped && (RecursiveDesiredExpanded?.Invoke(entry.Id) ?? projection.IsExpanded(entry.Id)))
                RecursiveExpansionRequested?.Invoke(this, (entry.Id, false, false));
            else if (node.ParentId != 0)
            {
                var parentView = projection.ViewIndexOfId(node.ParentId);
                if (parentView >= 0) MoveTo(parentView, false);
            }
            return true;
        }
        return false;
    }

    private IReadOnlyList<RecursiveContextAction>? RecursiveContextActions(bool background)
    {
        if (!_recursiveMode || _items.Binding?.Recursive is not { } projection) return null;
        var actions = new List<RecursiveContextAction>();
        if (background && RecursiveCanContinue?.Invoke() == true)
            actions.Add(new(StringTable.Get("Recursive_Continue"), "\uE768", () =>
            {
                if (_items.Binding?.Recursive?.SessionId == projection.SessionId) RecursiveContinueRequested?.Invoke();
            }));
        if (projection.IsFiltering) return actions.Count == 0 ? null : actions;
        var targets = background ? new[] { 0 } : _selection.Ids.Where(id => projection.IsVisible(id)
            && projection.GetNode(id).IsDirectory && !projection.GetNode(id).TraversalSkipped
            && projection.GetChildState(id, _items.Binding.RecursiveOperationsCurrent?.Invoke() == true) != RecursiveChildState.Empty).ToArray();
        if (targets.Length == 0) return actions.Count == 0 ? null : actions;
        bool Owns() => _recursiveMode && _items.Binding?.Recursive?.SessionId == projection.SessionId;
        bool Expanded(int id) => RecursiveDesiredExpanded?.Invoke(id) ?? projection.IsExpanded(id);
        // The same menu position always operates on the same captured scope.
        // A mixed selection folds when any selected directory is expanded.
        // The panel root itself is synthetic; inspect its visible directories.
        var collapse = background
            ? Enumerable.Range(0, projection.Index.Count).Any(view =>
            {
                var id = projection.Index[view] + 1;
                return projection.GetNode(id).IsDirectory && Expanded(id);
            })
            : targets.Any(Expanded);
        if (collapse)
            actions.Add(new(StringTable.Get("Recursive_CollapseMenu"), "\uE70D", () =>
            { if (Owns()) RecursiveDepthRequested?.Invoke(this, (targets, 0)); }));
        else
        {
            var choices = Enumerable.Range(1, 5).Append(int.MaxValue).Select(depth => new RecursiveContextAction(
                depth == int.MaxValue ? StringTable.Get("Recursive_DepthAll") : StringTable.Format("Recursive_DepthLevels", depth), "", () =>
                { if (Owns()) RecursiveDepthRequested?.Invoke(this, (targets, depth)); })).ToArray();
            actions.Add(new(StringTable.Get("Recursive_DepthMenu"), "\uE76C", () => { }, choices));
        }
        return actions;
    }

    private void RetireStaleRecursivePresenters(EntryStore store, EntryViewIndex index, long bindingVersion)
    {
        var children = FileItemElementFactory.PresentationChildren(Repeater).OfType<FileRow>().ToArray();
        foreach (var row in children)
        {
            if (_resourcesReleased || bindingVersion != _bindingVersion) return;
            var view = Repeater.GetElementIndex(row);
            if (view >= 0 && view < index.Count && row.ViewIndex == view
                && row.EntryId == index.GetEntry(store, view).Id) continue;
            RetireRecursivePresenter(row);
        }
    }

    private void RetireRecursivePresenter(FileRow row)
    {
        if (ReferenceEquals(row, _renameRow)) CancelInlineRename();
        if (_nameRenameClicks.TracksEntry(row.EntryId)) CancelNameClickRename();
        if (_preparations.TryGetValue(row, out var stamp)) ++stamp.Version;
        row.SelectionToggleRequested -= OnSelectionToggleRequested;
        _realized.Remove(row);
        row.ClearForRecycle();
    }

    private void RefreshRealizedRecursivePresentation(long bindingVersion)
    {
        // Native pinned children may outlive Reset without ElementClearing.
        // Inspect actual container indices, retire every invalid hit area, and
        // advance retained rows into the same viewport/resource epoch.
        ObserveThumbnailViewport();
        var revision = _items.Revision;
        var resolvePath = ResolvePath;
        var children = FileItemElementFactory.PresentationChildren(Repeater).OfType<FileRow>().ToArray();
        foreach (var row in children)
        {
            if (_resourcesReleased || bindingVersion != _bindingVersion || revision != _items.Revision) return;
            var view = Repeater.GetElementIndex(row);
            if (view < 0 || !_items.TryGetEntry(view, out var entry))
            {
                RetireRecursivePresenter(row);
                continue;
            }
            if (row.ViewIndex != view || row.EntryId != entry.Id
                || row.ThumbnailContentEpoch != _thumbnailContentEpoch)
            {
                var path = resolvePath?.Invoke(entry);
                var tags = ResolveTags?.Invoke(entry);
                if (_resourcesReleased || bindingVersion != _bindingVersion || revision != _items.Revision) return;
                ApplyRowColumns(row, view);
                ConfigureRecursiveRow(row, entry);
                row.BindViewport(_thumbnailRetentionOwner, _thumbnailContentEpoch, ThumbnailModeFor(view),
                    view, entry, _selection.Contains(entry.Id), path, _folderSizeCts.Token, _retainKnownFolderSizes);
                if (_resourcesReleased || bindingVersion != _bindingVersion || revision != _items.Revision
                    || Repeater.GetElementIndex(row) != view) return;
                row.SetNameHighlightQuery(_nameHighlightQuery);
                row.SetTags(tags);
                row.SetDropTarget(view == _dropTargetViewIndex);
                _realized.Add(row);
                row.SelectionToggleRequested -= OnSelectionToggleRequested;
                row.SelectionToggleRequested += OnSelectionToggleRequested;
            }
            else
            {
                ConfigureRecursiveRow(row, entry);
                row.UpdateThumbnailDemand(ThumbnailModeFor(view), _thumbnailRetentionOwner,
                    _thumbnailDecision.Retain.Contains(view));
            }
            if (_resourcesReleased || bindingVersion != _bindingVersion || revision != _items.Revision) return;
            row.PresentBoundEntry();
        }
    }
    private bool OwnsRecursiveArrow(FileRow row, RecursiveProjection projection, int id) => !_resourcesReleased
        && row.EntryId == id && _recursiveMode && _items.Binding?.Recursive?.SessionId == projection.SessionId
        && !_resourcesReleased && _items.Binding.Generation == _generation;

    private void ConfigureRecursiveRow(FileRow row, FileEntryCore entry)
    {
        if (_items.Binding?.Recursive is { } projection)
            row.SetRecursivePresentation(projection.GetNode(entry.Id), projection.IsExpanded(entry.Id),
                projection.IsContextOnly(entry.Id), projection.IsFiltering ? null : id => { if (OwnsRecursiveArrow(row, projection, id))
                { CancelNameClickRename(); FilesMate.App.Input.ItemActivation.CancelPendingClick(this); RecursiveToggleRequested?.Invoke(this, id); } },
                projection.ResolveRelativeParentPath(entry.Id) is { Length: > 0 } parent ? parent : ".",
                projection.IsOperationSafe(entry.Id, RecursiveOperationScope.IdentityOnly)
                    && _items.Binding.RecursiveOperationsCurrent?.Invoke() == true,
                projection.IsFiltering ? null : id =>
                {
                    if (OwnsRecursiveArrow(row, projection, id) && projection.IsVisible(id) && !projection.GetNode(id).TraversalSkipped)
                    {
                        CancelNameClickRename(); FilesMate.App.Input.ItemActivation.CancelPendingClick(this);
                        RecursiveAltDepthRequested?.Invoke(this, (id, RecursiveAltExpansionDepth));
                    }
                }, projection.GetChildState(entry.Id, _items.Binding.RecursiveOperationsCurrent?.Invoke() == true), _items.Binding.RecursiveOperationsCurrent?.Invoke() == true);
        else row.SetRecursivePresentation(null, false, false, null);
    }
}
