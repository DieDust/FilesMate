using System.Collections;
using System.Collections.Specialized;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Controls.FileSurface;

/// <summary>
/// Index-based items source for the details surface. Does not wrap files in view-models.
/// One collection change per publish (batch or generation), never one per file.
/// Recycled presenter elements must reset visual state in ElementClearing; this source never stores row UI.
/// </summary>
public sealed class EntryItemsSource : IList, INotifyCollectionChanged
{
    private static readonly NotifyCollectionChangedEventArgs ResetArgs = new(NotifyCollectionChangedAction.Reset);

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public EntryStore? Store { get; private set; }

    public EntryViewIndex? Index { get; private set; }

    public long Generation { get; private set; }

    public int Count => Index?.Count ?? 0;

    public bool IsReadOnly => true;

    public bool IsFixedSize => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public object? this[int index]
    {
        get => index;
        set => throw new NotSupportedException();
    }

    public bool TryPublish(EntryStore store, EntryViewIndex index, long currentGeneration)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        if (index.Generation != currentGeneration)
        {
            return false;
        }

        Store = store;
        Index = index;
        Generation = index.Generation;
        CollectionChanged?.Invoke(this, ResetArgs);
        return true;
    }

    public void ClearView()
    {
        if (Store is null && Index is null && Generation == 0)
        {
            return;
        }

        Store = null;
        Index = null;
        Generation = 0;
        CollectionChanged?.Invoke(this, ResetArgs);
    }

    public bool TryGetEntry(int viewIndex, out FileEntryCore entry)
    {
        entry = default;
        if (Store is null || Index is null || (uint)viewIndex >= (uint)Index.Count)
        {
            return false;
        }

        entry = Store[Index[viewIndex]];
        return true;
    }

    public bool TryFindEntry(Func<FileEntryCore, bool> match, out FileEntryCore entry)
    {
        ArgumentNullException.ThrowIfNull(match);
        for (var view = 0; view < Count; view++)
        {
            if (TryGetEntry(view, out entry) && match(entry))
                return true;
        }

        entry = default;
        return false;
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => ClearView();

    public bool Contains(object? value) => IndexOf(value) >= 0;

    public int IndexOf(object? value)
    {
        if (value is int i && (uint)i < (uint)Count)
        {
            return i;
        }

        return -1;
    }

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        var n = Count;
        for (var i = 0; i < n; i++)
        {
            array.SetValue(i, index + i);
        }
    }

    public IEnumerator GetEnumerator()
    {
        var n = Count;
        for (var i = 0; i < n; i++)
        {
            yield return i;
        }
    }
}
