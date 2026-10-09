using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Core.Diagnostics;
using FilesMate.Core.Operations;
using FilesMate.Core.Recursive;

namespace FilesMate.App.Navigation;

public sealed partial class PaneViewModel
{
    private DepthUndoGroup? _pendingDepthChange;
    private FileUndoRecord? _pendingDepthRecord;
    private bool _applyingDepthHistory;

    public void OpenRecursiveNode(int id)
    {
        if (IsRecursiveNodeDesiredExpanded(id) && _recursiveProjection?.HasChildren(id) == true) SetRecursiveExpanded(id, false);
        else if (App.ExplorerPreferences.RecursiveAutoExpandDepth is int depth && depth > 0) SetRecursiveDepth([id], depth);
        else SetRecursiveExpanded(id, true);
    }

    public void SetRecursiveDepth(IReadOnlyList<int> ids, int depth, bool expandOnly = false)
    {
        AssertUi();
        if (_recursiveHoldingView) { StatusText = StringTable.Get("Recursive_Checking"); return; }
        if (depth is < 0 or > 5 && depth != int.MaxValue) throw new ArgumentOutOfRangeException(nameof(depth));
        if (_disposed || !IsRecursive || RecursiveFilterRevealsAncestors || _recursiveProjection is not { } projection
            || _recursiveSession?.CurrentSnapshot is not { } snapshot) return;
        if (FileOperationLifetime.IsBusy || App.FileUndo.IsApplying) { ReportUserError(StringTable.Get("Files_Busy")); return; }
        var selected = ids.ToHashSet();
        foreach (var id in selected)
        {
            if (id < 0 || id > snapshot.Count || id != 0 && (!projection.IsVisible(id) || !snapshot.GetNode(id).IsDirectory)) return;
            if (id != 0 && snapshot.GetNode(id).TraversalSkipped) { ReportUserError(StringTable.Get("Recursive_NotTraversed")); return; }
        }
        if (depth > 0) selected.RemoveWhere(id => id != 0 && projection.GetChildState(id, !_recursiveStale) == RecursiveChildState.Empty);
        var scopes = RecursiveDepthScopes.Outermost(snapshot, selected);
        if (scopes.Count == 0) return;
        if (depth > 0) foreach (var scope in scopes) RequestRecursiveTraversal(scope);
        CancelPendingDepth(queue: false);
        if (_disposed || !IsRecursive || _recursiveSession?.SessionId != snapshot.SessionId) return;
        var changes = new List<ExpansionUndo>();
        foreach (var id in scopes)
            if (!RecordRecursiveExpansion(snapshot, id, depth != 0, true, depth, changes, expandOnly))
            {
                foreach (var prior in changes.AsEnumerable().Reverse()) prior.Restore(false);
                QueueRecursiveProjection(semanticChange: true);
                return;
            }
        if (changes.Count == 0)
        {
            if (depth != 0 && _recursiveTraversalStopped) RecoverStoppedRecursiveScan();
            return;
        }
        var group = new DepthUndoGroup(this, changes.ToArray());
        var operation = PerformanceTrace.NextOperation();
        // Record at input time, before a later file action can overtake this view action.
        _pendingDepthChange = group;
        _pendingDepthRecord = FileUndoRecord.View(group);
        App.FileUndo.Push(_pendingDepthRecord);
        if (!ReferenceEquals(_pendingDepthChange, group)) return;
        StatusText = StringTable.Get(depth == 0 ? "Recursive_Collapsing" : "Recursive_Expanding");
        if (_disposed || !IsRecursive || _recursiveSession?.SessionId != snapshot.SessionId) return;
        QueueRecursiveProjection(semanticChange: true, operation: operation, preferPublishedSnapshot: true, keepPendingDepth: true);
        if (depth != 0 && _recursiveTraversalStopped) { CommitPendingDepth(); RecoverStoppedRecursiveScan(); }
        else if (depth != 0 && snapshot.State == RecursiveSessionState.Paused && CanContinueRecursiveScan) ContinueRecursiveScan();
    }

    private void CommitPendingDepth()
    {
        _pendingDepthChange = null;
        _pendingDepthRecord = null;
    }

    private bool CancelPendingDepth(bool queue = true)
    {
        if (_pendingDepthChange is not { } change || App.FileUndo.IsApplying) return false;
        var record = _pendingDepthRecord;
        var epoch = _recursiveEpoch; var sessionId = _recursiveSession?.SessionId;
        _pendingDepthChange = null; _pendingDepthRecord = null;
        _activeRecursiveBuild?.Cancellation.Cancel();
        _pendingRecursiveBuild?.Cancellation.Dispose(); _pendingRecursiveBuild = null;
        change.RestoreUnpublished();
        if (record is not null) App.FileUndo.TryDiscardPendingView(record);
        if (queue && epoch == _recursiveEpoch && sessionId == _recursiveSession?.SessionId) QueueRecursiveProjection(semanticChange: true, preferPublishedSnapshot: true);
        return true;
    }

    private sealed class DepthUndoGroup(PaneViewModel owner, ExpansionUndo[] changes) : IViewUndoChange
    {
        private readonly WeakReference<PaneViewModel> _owner = new(owner);
        public string Description => StringTable.Get("Recursive_DepthMenu");
        public void RestoreUnpublished() { foreach (var change in changes.Reverse()) change.Restore(false); }
        public void Apply(bool redo)
        {
            if (!_owner.TryGetTarget(out var pane)) throw new InvalidOperationException(StringTable.Get("Recursive_UndoUnavailable"));
            if (ReferenceEquals(pane._pendingDepthChange, this)) pane.CommitPendingDepth();
            foreach (var change in changes) change.Validate(); // Atomic validation before any selected scope mutates.
            pane._applyingDepthHistory = true;
            try { foreach (var change in redo ? changes : changes.Reverse()) change.Apply(redo); }
            finally { pane._applyingDepthHistory = false; }
            pane.QueueRecursiveProjection(semanticChange: true, preferPublishedSnapshot: true);
        }
    }
}
