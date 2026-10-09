using FilesMate.App.Localization;
using FilesMate.Core.Operations;
using FilesMate.Core.Recursive;

namespace FilesMate.App.Navigation;

public sealed partial class PaneViewModel
{
    private Guid _recursiveHistoryScope = Guid.NewGuid();
    private Guid _restoredExpansionSession;
    private int _restoredExpansionCount;
    private long _recursiveViewRetainedBytes;
    private readonly Dictionary<ExpansionKey, ExpansionTarget> _expansionTargets = [];
    private readonly Dictionary<string, List<ExpansionTarget>> _expansionLeaves = new(StringComparer.Ordinal);

    private readonly record struct ExpansionKey(string RelativePath, long CreatedUtcTicks, bool Subtree);
    private sealed class ExpansionTarget(ExpansionKey key, Guid originalSession)
    {
        internal ExpansionKey Key { get; } = key;
        internal string[] Segments { get; } = key.RelativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        internal Guid OriginalSession { get; } = originalSession;
        internal RecursiveExpansionIntent? Intent;
        internal int? ResolvedId;
    }

    private void ResetRecursiveHistoryScope()
    {
        _recursiveHistoryScope = Guid.NewGuid();
        _restoredExpansionSession = Guid.Empty;
        _restoredExpansionCount = 0;
        _recursiveViewRetainedBytes = 0;
        _expansionTargets.Clear();
        _expansionLeaves.Clear();
    }

    private bool RecordRecursiveExpansion(RecursiveSnapshot snapshot, int id, bool expanded, bool subtree, int? depth = null, List<ExpansionUndo>? batch = null, bool expandOnly = false)
    {
        RestoreRecursiveIntents(snapshot);
        var key = new ExpansionKey(id == 0 ? "" : snapshot.ResolveRelativePath(id),
            id == 0 ? 0 : snapshot.GetEntry(id).CreatedUtcTicks, subtree);
        if (!_expansionTargets.TryGetValue(key, out var target))
        {
            // Retain exact paths for safe history rebase only after accounting their
            // string/segment/container ownership and three intent-publication copies.
            var retainedBytes = 512L + 8L * key.RelativePath.Length + 48L * (id == 0 ? 0 : snapshot.GetNode(id).Depth + 1);
            if (_recursiveSession?.TryReserveViewState(retainedBytes) != true)
            { ReportUserError(StringTable.Get("Recursive_ViewMemoryBudget")); return false; }
            _recursiveViewRetainedBytes += retainedBytes;
            target = new(key, snapshot.SessionId);
            _expansionTargets.Add(key, target);
            if (target.Segments.Length > 0)
            {
                var leaf = target.Segments[^1];
                if (!_expansionLeaves.TryGetValue(leaf, out var targets)) _expansionLeaves[leaf] = targets = [];
                targets.Add(target);
            }
        }
        target.ResolvedId = id;
        var change = depth is int levels ? (expandOnly ? _recursiveExpansionState.EnsureDepthWithUndo(snapshot, id, levels) : _recursiveExpansionState.SetDepthWithUndo(snapshot, id, levels))
            : _recursiveExpansionState.SetExpandedWithUndo(id, expanded, subtree);
        if (change.Before is { } before && before == change.After) return true;
        target.Intent = change.After;
        var undo = new ExpansionUndo(this, _recursiveHistoryScope, snapshot.RootPath, target, change.Before, change.After);
        if (batch is not null) batch.Add(undo);
        else App.FileUndo.Push(FileUndoRecord.View(undo));
        return true;
    }

    // Same-root refresh may change session-local ids. Compare exact parent names
    // and captured creation time; never substitute a leaf name in another folder.
    // Each new entry is checked once, only when a projection is allowed to start.
    private void RestoreRecursiveIntents(RecursiveSnapshot snapshot)
    {
        if (_restoredExpansionSession != snapshot.SessionId)
        {
            _recursiveExpansionState.Clear();
            _restoredExpansionSession = snapshot.SessionId;
            _restoredExpansionCount = 0;
            foreach (var target in _expansionTargets.Values) target.ResolvedId = null;
            foreach (var target in _expansionTargets.Values.Where(t => t.Segments.Length == 0))
            {
                target.ResolvedId = 0;
                _recursiveExpansionState.RestoreIntent(0, target.Key.Subtree, target.Intent);
            }
        }
        if (_expansionTargets.Count == 0) { _restoredExpansionCount = snapshot.Count; return; }
        for (var id = _restoredExpansionCount + 1; id <= snapshot.Count; id++)
        {
            var entry = snapshot.GetEntry(id);
            if (!_expansionLeaves.TryGetValue(entry.Name, out var targets) || !snapshot.GetNode(id).IsDirectory) continue;
            foreach (var target in targets)
            {
                if (target.ResolvedId is not null || target.Key.CreatedUtcTicks != entry.CreatedUtcTicks
                    || target.Key.CreatedUtcTicks == 0 && target.OriginalSession != snapshot.SessionId) continue;
                var current = id;
                var matches = true;
                for (var i = target.Segments.Length - 1; i >= 0; i--)
                {
                    if (current == 0 || !string.Equals(snapshot.GetEntry(current).Name, target.Segments[i], StringComparison.Ordinal))
                    { matches = false; break; }
                    current = snapshot.GetNode(current).ParentId;
                }
                if (!matches || current != 0) continue;
                target.ResolvedId = id;
                _recursiveExpansionState.RestoreIntent(id, target.Key.Subtree, target.Intent);
            }
        }
        _restoredExpansionCount = snapshot.Count;
    }

    private sealed class ExpansionUndo(PaneViewModel owner, Guid scope, string root, ExpansionTarget target,
        RecursiveExpansionIntent? before, RecursiveExpansionIntent after) : IViewUndoChange
    {
        private readonly WeakReference<PaneViewModel> _owner = new(owner);
        public string Description => StringTable.Get(after.Expanded
            ? target.Key.Subtree ? "Recursive_ExpandSubtree" : "Recursive_Expand"
            : target.Key.Subtree ? "Recursive_CollapseSubtree" : "Recursive_Collapse");
        public void Validate()
        {
            if (!_owner.TryGetTarget(out var pane) || pane._disposed || !pane.IsRecursive
                || pane._recursiveHistoryScope != scope || pane._recursiveStale || pane._recursiveHoldingView
                || !string.Equals(pane.AddressText, root, StringComparison.Ordinal)
                || pane._recursiveSession?.CurrentSnapshot is not { } snapshot)
                throw new InvalidOperationException(StringTable.Get("Recursive_UndoUnavailable"));
            pane.AssertUi();
            pane.RestoreRecursiveIntents(snapshot);
            if (target.ResolvedId is not int id || id > snapshot.Count
                || id != 0 && (!snapshot.GetNode(id).IsDirectory || snapshot.GetNode(id).TraversalSkipped
                    || snapshot.GetEntry(id).CreatedUtcTicks != target.Key.CreatedUtcTicks))
                throw new InvalidOperationException(StringTable.Get("Recursive_UndoUnavailable"));
        }
        public void Restore(bool redo)
        {
            if (!_owner.TryGetTarget(out var pane) || target.ResolvedId is not int id) return;
            target.Intent = redo ? after : before;
            pane._recursiveExpansionState.RestoreIntent(id, target.Key.Subtree, target.Intent);
        }
        public void Apply(bool redo)
        {
            Validate();
            if (!_owner.TryGetTarget(out var pane)) return;
            Restore(redo);
            // Exact prior sequence preserves mixed subtree/row precedence. Restoring
            // an older bool with a new sequence would incorrectly overwrite children.
            if (!pane._applyingDepthHistory) pane.QueueRecursiveProjection(semanticChange: true);
        }
    }
}
