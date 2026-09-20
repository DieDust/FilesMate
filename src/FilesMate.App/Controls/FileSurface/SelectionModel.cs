using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

/// <summary>
/// Selection by stable entry id. Survives sort/filter because ids do not change.
/// </summary>
public sealed class SelectionModel
{
    private readonly HashSet<int> _ids = [];

    public int Count => _ids.Count;

    public int? AnchorId { get; private set; }

    public int? PrimaryId { get; private set; }

    public bool Contains(int id) => _ids.Contains(id);

    public IReadOnlyCollection<int> Ids => _ids;

    public void Clear()
    {
        _ids.Clear();
        AnchorId = null;
        PrimaryId = null;
    }

    public void SelectOnly(int id)
    {
        _ids.Clear();
        _ids.Add(id);
        AnchorId = id;
        PrimaryId = id;
    }

    public void Toggle(int id)
    {
        if (!_ids.Remove(id))
        {
            _ids.Add(id);
        }

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
            _ids.Add(store[index[i]].Id);
        }

        PrimaryId = store[index[Math.Clamp(toView, 0, index.Count - 1)]].Id;
        AnchorId ??= store[index[Math.Clamp(fromView, 0, index.Count - 1)]].Id;
    }

    public void SelectAll(EntryStore store, EntryViewIndex index)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        Clear();
        for (var i = 0; i < index.Count; i++)
        {
            _ids.Add(store[index[i]].Id);
        }

        if (index.Count > 0)
        {
            PrimaryId = store[index[0]].Id;
            AnchorId ??= PrimaryId;
        }
    }

    public void ReplaceFromViewIndices(EntryStore store, EntryViewIndex index, IReadOnlyList<int> views)
    {
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

            var id = store[index[view]].Id;
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
    }

    public int ViewIndexOfPrimary(EntryStore store, EntryViewIndex index)
    {
        if (PrimaryId is not int id)
        {
            return -1;
        }

        return index.IndexOfId(store, id);
    }

    public bool RemoveMissing(EntryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (_ids.Count == 0) return false;
        // Only allocate for the selection, not for every file in a large folder.
        var missing = new HashSet<int>(_ids);
        if (PrimaryId is int focused) missing.Add(focused);
        if (AnchorId is int anchored) missing.Add(anchored);
        store.Observe(entries =>
        {
            foreach (var entry in entries)
            {
                missing.Remove(entry.Id);
                if (missing.Count == 0) break;
            }
            return 0;
        });
        if (missing.Count == 0) return false;
        _ids.ExceptWith(missing);
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
        ReplaceFromViewIndices(store, index, Enumerable.Range(0, index.Count).Where(i => !previous.Contains(store[index[i]].Id)).ToArray());
    }

    public void SelectSameType(EntryStore store, EntryViewIndex index)
    {
        var primary = ViewIndexOfPrimary(store, index);
        if (primary < 0) return;
        var source = store[index[primary]];
        var extension = Path.GetExtension(source.Name);
        ReplaceFromViewIndices(store, index, Enumerable.Range(0, index.Count).Where(i =>
        {
            var entry = store[index[i]];
            return entry.Kind == source.Kind && (entry.Kind == EntryKind.Directory
                || string.Equals(Path.GetExtension(entry.Name), extension, StringComparison.OrdinalIgnoreCase));
        }).ToArray());
    }
}
