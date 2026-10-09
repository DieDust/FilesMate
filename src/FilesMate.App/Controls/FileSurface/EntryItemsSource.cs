using System.Collections;
using System.Collections.Specialized;

using FilesMate.App.Navigation;
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

    public EntryViewBinding? Binding { get; private set; }

    /// <summary>Changes before notifications, including a reentrant publish or clear.</summary>
    public long Revision { get; private set; }
    internal long ResetNotificationCount { get; private set; }

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

    public bool TryPublish(EntryStore store, EntryViewIndex index, long currentGeneration, bool append = false,
        EntryViewBinding? binding = null)
        => TryPublishCore(store, index, currentGeneration, append, binding, default);

    /// <summary>
    /// Shares the exact immutable-row comparison with the surface's pre-publication
    /// thumbnail invalidation. The value is local to that bind; no prior snapshots
    /// are retained in a cache or trusted after a reentrant publish/clear.
    /// </summary>
    internal SourcePrefixComparison CompareSourcePrefix(EntryStore store, EntryViewIndex index, long generation, EntrySourcePrefixProof? proof = null)
        => new(this, store, index, generation, proof);

    internal bool TryPublishCompared(EntryStore store, EntryViewIndex index, long currentGeneration, bool append,
        EntryViewBinding? binding, in SourcePrefixComparison comparison)
        => TryPublishCore(store, index, currentGeneration, append, binding, comparison);

    private bool TryPublishCore(EntryStore store, EntryViewIndex index, long currentGeneration, bool append,
        EntryViewBinding? binding, in SourcePrefixComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(index);
        if (index.Generation != currentGeneration || !index.CanPublish(store)
            || binding is not null && (!ReferenceEquals(binding.Store, store)
                || !ReferenceEquals(binding.Index, index) || binding.Generation != currentGeneration)
            || (ReferenceEquals(Store, store) && Generation == currentGeneration
                && Index is not null && index.DataVersion < Index.DataVersion))
        {
            return false;
        }

        var previousIndex = Index;
        var previousCount = previousIndex?.Count ?? 0;
        // Stable IDs distinguish a metadata update from a reordered/replaced row.
        var samePrefix = ReferenceEquals(Store, store) && Generation == currentGeneration
            && previousIndex is not null
            && (index.Count == previousCount || (append && index.Count > previousCount))
            && (comparison.IsValidFor(this, store, index, currentGeneration)
                ? comparison.SamePrefix : index.HasSameSourcePrefix(previousIndex));
        var sameView = samePrefix && index.Count == previousCount;
        var appended = append && samePrefix;
        var range = binding?.SourcePrefixProof;
        var splice = !sameView && ReferenceEquals(Store, store) && Generation == currentGeneration
            && Binding?.Recursive is { IsFiltering: false } previousTree
            && binding?.Recursive is { IsFiltering: false } currentTree && previousTree.SessionId == currentTree.SessionId
            && range?.Matches(index, previousIndex) == true && (range.HasSingleRangeChange || range.HasRangeReplacement);
        Store = store;
        Index = index;
        Generation = index.Generation;
        Binding = binding;
        Revision++;
        if (splice && range!.HasRangeReplacement)
        {
            // Alt top-up can insert grandchildren between already visible
            // siblings. Rebuild only that affected interval, retaining native
            // containers before/after it instead of resetting the whole view.
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace,
                Enumerable.Range(range.RangeStart, range.AddedCount).ToArray(),
                Enumerable.Range(range.RangeStart, range.RemovedCount).ToArray(), range.RangeStart));
        }
        else if (splice)
        {
            var added = range!.AddedCount > 0;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                added ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Remove,
                Enumerable.Range(range.RangeStart, added ? range.AddedCount : range.RemovedCount).ToArray(), range.RangeStart));
        }
        else if (!appended && !sameView)
        {
            ResetNotificationCount++;
            FilesMate.Core.Diagnostics.PerformanceTrace.Record(FilesMate.Core.Diagnostics.PerformanceStage.RecursiveCollectionReset,
                generation: currentGeneration, operation: binding?.DiagnosticOperation ?? 0, count: index.Count);
            CollectionChanged?.Invoke(this, ResetArgs);
        }
        else if (index.Count > previousCount)
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                Enumerable.Range(previousCount, index.Count - previousCount).ToArray(), previousCount));
        return true;
    }

    internal readonly struct SourcePrefixComparison
    {
        private readonly EntryItemsSource _source;
        private readonly EntryStore _store;
        private readonly EntryViewIndex? _previousIndex;
        private readonly EntryViewIndex _candidateIndex;
        private readonly long _generation;
        private readonly long _revision;

        internal SourcePrefixComparison(EntryItemsSource source, EntryStore store, EntryViewIndex index, long generation, EntrySourcePrefixProof? proof)
        {
            _source = source;
            _store = store;
            _previousIndex = source.Index;
            _candidateIndex = index;
            _generation = generation;
            _revision = source.Revision;
            SamePrefix = ReferenceEquals(source.Store, store) && source.Generation == generation
                && _previousIndex is not null && (proof?.Matches(index, _previousIndex) == true
                    ? proof.SamePrefix : index.HasSameSourcePrefix(_previousIndex));
        }

        internal bool SamePrefix { get; }

        internal bool IsValidFor(EntryItemsSource source, EntryStore store, EntryViewIndex index, long generation)
            => ReferenceEquals(_source, source) && _revision == source.Revision
                && ReferenceEquals(_previousIndex, source.Index) && ReferenceEquals(_store, store)
                && ReferenceEquals(_candidateIndex, index) && _generation == generation;
    }

    public void ClearView()
    {
        if (Store is null && Index is null && Generation == 0 && Binding is null)
        {
            return;
        }

        Store = null;
        Index = null;
        Generation = 0;
        Binding = null;
        Revision++;
        CollectionChanged?.Invoke(this, ResetArgs);
    }

    public bool TryGetEntry(int viewIndex, out FileEntryCore entry)
    {
        entry = default;
        if (Store is null || Index is null || (uint)viewIndex >= (uint)Index.Count)
        {
            return false;
        }

        entry = Index.GetEntry(Store, viewIndex);
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
