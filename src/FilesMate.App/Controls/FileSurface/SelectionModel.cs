using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

/// <summary>
/// Selection by stable entry id. Survives sort; filtering removes hidden identities.
/// </summary>
public sealed class SelectionModel
{
    private readonly HashSet<int> _ids = [];
    private readonly LinkedList<int> _leadHistory = new();
    private readonly Dictionary<int, LinkedListNode<int>> _leadNodes = [];

    public int Count => _ids.Count;
    public long Revision { get; private set; }

    public int? AnchorId { get; private set; }

    public int? PrimaryId { get; private set; }

    // Keyboard focus may remain on an item removed with Ctrl+click. Display
    // location follows only explicit operations on identities still selected.
    public int? SelectedLeadId => _leadHistory.Last?.Value;

    private void RecordSelectedLead(int id)
    {
        if (_leadNodes.Remove(id, out var previous)) _leadHistory.Remove(previous);
        _leadNodes[id] = _leadHistory.AddLast(id);
    }

    private void PruneSelectedLead()
    {
        for (var node = _leadHistory.First; node is not null;)
        {
            var next = node.Next;
            if (!_ids.Contains(node.Value)) { _leadNodes.Remove(node.Value); _leadHistory.Remove(node); }
            node = next;
        }
    }

    public bool Contains(int id) => _ids.Contains(id);

    public IReadOnlyCollection<int> Ids => _ids;

    public void Rebase(Func<int, int> map)
    {
        var selected = _ids.Select(map).Where(id => id > 0).ToArray();
        var leads = _leadHistory.Select(map).Where(id => id > 0).ToArray();
        var anchor = AnchorId is int a ? map(a) : -1;
        var primary = PrimaryId is int p ? map(p) : -1;
        Clear();
        foreach (var id in selected) _ids.Add(id);
        foreach (var id in leads) if (_ids.Contains(id)) RecordSelectedLead(id);
        AnchorId = anchor > 0 ? anchor : null;
        PrimaryId = primary > 0 ? primary : null;
    }

    public void Clear()
    {
        Revision++;
        _ids.Clear();
        _leadHistory.Clear();
        _leadNodes.Clear();
        AnchorId = null;
        PrimaryId = null;
    }

    public void SelectOnly(int id)
    {
        Revision++;
        _ids.Clear();
        _ids.Add(id);
        PruneSelectedLead();
        RecordSelectedLead(id);
        AnchorId = id;
        PrimaryId = id;
    }

    public void Toggle(int id)
    {
        Revision++;
        if (!_ids.Remove(id))
        {
            _ids.Add(id);
            RecordSelectedLead(id);
        }
        else PruneSelectedLead();

        PrimaryId = id;
        AnchorId ??= id;
        if (_ids.Count == 0)
        {
            AnchorId = null;
            PrimaryId = null;
        }
    }

    public void SelectRange(EntryStore store, EntryViewIndex index, int fromView, int toView)
    {
        Revision++;
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        if (index.Count == 0)
        {
            return;
        }

        var lo = Math.Clamp(Math.Min(fromView, toView), 0, index.Count - 1);
        var hi = Math.Clamp(Math.Max(fromView, toView), 0, index.Count - 1);
        _ids.Clear();
        for (var i = lo; i <= hi; i++)
        {
            _ids.Add(index.GetEntry(store, i).Id);
        }

        PrimaryId = index.GetEntry(store, Math.Clamp(toView, 0, index.Count - 1)).Id;
        PruneSelectedLead();
        RecordSelectedLead(PrimaryId.Value);
        AnchorId ??= index.GetEntry(store, Math.Clamp(fromView, 0, index.Count - 1)).Id;
    }

    public void SelectAll(EntryStore store, EntryViewIndex index)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        Clear();
        for (var i = 0; i < index.Count; i++)
        {
            _ids.Add(index.GetEntry(store, i).Id);
        }

        if (index.Count > 0)
        {
            PrimaryId = index.GetEntry(store, 0).Id;
            RecordSelectedLead(PrimaryId.Value);
            AnchorId ??= PrimaryId;
        }
    }

    public void ReplaceFromViewIndices(EntryStore store, EntryViewIndex index, IReadOnlyList<int> views)
    {
        Revision++;
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(views);
        _ids.Clear();
        PrimaryId = null;
        var first = true;
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            if ((uint)view >= (uint)index.Count)
            {
                continue;
            }

            var id = index.GetEntry(store, view).Id;
            if (!_ids.Add(id))
            {
                continue;
            }

            if (first)
            {
                AnchorId = id;
                first = false;
            }

            PrimaryId = id;
        }

        if (_ids.Count == 0)
        {
            AnchorId = null;
        }
        PruneSelectedLead();
        if (PrimaryId is int lead) RecordSelectedLead(lead);
    }

    public int ViewIndexOfPrimary(EntryStore store, EntryViewIndex index)
    {
        if (PrimaryId is not int id)
        {
            return -1;
        }

        return index.IndexOfId(store, id);
    }

    public bool RemoveWhere(Func<int, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var changed = _ids.RemoveWhere(id => predicate(id)) > 0;
        if (PrimaryId is int primary && predicate(primary)) { PrimaryId = null; changed = true; }
        if (AnchorId is int anchor && predicate(anchor)) { AnchorId = null; changed = true; }
        if (_ids.Count == 0) { PrimaryId = null; AnchorId = null; }
        else { PrimaryId ??= _ids.Min(); AnchorId ??= PrimaryId; }
        if (changed) Revision++;
        if (changed) PruneSelectedLead();
        return changed;
    }

    public bool RemoveOutsideView(EntryViewIndex index)
    {
        if (_ids.Count == 0) return false;
        // Memory proportional to selection; one view pass avoids an index scan
        // per selected id in large directories. Context ancestors are pruned by
        // the recursive projection's existing selectable-identity gate.
        var missing = new HashSet<int>(_ids);
        if (PrimaryId is int primary) missing.Add(primary);
        if (AnchorId is int anchor) missing.Add(anchor);
        foreach (var source in index)
        {
            missing.Remove(index.Snapshot[source].Id);
            if (missing.Count == 0) break;
        }
        return RemoveWhere(missing.Contains);
    }

    public bool RemoveMissing(EntryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return RemoveMissing(store.CaptureSnapshot());
    }

    public bool RemoveMissing(EntrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_ids.Count == 0) return false;
        // Only allocate for the selection, not for every file in a large folder.
        var missing = new HashSet<int>(_ids);
        if (PrimaryId is int focused) missing.Add(focused);
        if (AnchorId is int anchored) missing.Add(anchored);
        for (var i = 0; i < snapshot.Count; i++)
        {
            missing.Remove(snapshot[i].Id);
            if (missing.Count == 0) break;
        }
        if (missing.Count == 0) return false;
        Revision++;
        _ids.ExceptWith(missing);
        PruneSelectedLead();
        if (_ids.Count == 0) Clear();
        else
        {
            if (PrimaryId is not int primary || missing.Contains(primary)) PrimaryId = _ids.Min();
            if (AnchorId is not int anchor || missing.Contains(anchor)) AnchorId = PrimaryId;
        }
        return true;
    }

    public void Invert(EntryStore store, EntryViewIndex index)
    {
        var previous = new HashSet<int>(_ids);
        ReplaceFromViewIndices(store, index, Enumerable.Range(0, index.Count).Where(i => !previous.Contains(index.GetEntry(store, i).Id)).ToArray());
    }

    public void SelectSameType(EntryStore store, EntryViewIndex index)
    {
        var primary = ViewIndexOfPrimary(store, index);
        if (primary < 0) return;
        var source = index.GetEntry(store, primary);
        var extension = Path.GetExtension(source.Name);
        ReplaceFromViewIndices(store, index, Enumerable.Range(0, index.Count).Where(i =>
        {
            var entry = index.GetEntry(store, i);
            return entry.Kind == source.Kind && (entry.Kind == EntryKind.Directory
                || HasSameExtension(entry.Name, extension));
        }).ToArray());
    }

    private static bool HasSameExtension(string? name, string? extension)
    {
        // Keep Path.GetExtension(string)'s null distinction for default/modified snapshots.
        if (name is null) return extension is null;
        return extension is not null
            && Path.GetExtension(name.AsSpan()).Equals(extension.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }
}
