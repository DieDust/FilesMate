using System.Buffers;
using System.Collections;

using FilesMate.Core.Directories;

namespace FilesMate.Core.Entries;

public sealed record NameSection(string Label, int FirstIndex, bool IsDirectory);

/// <summary>
/// Immutable integer index paired with the exact immutable entry snapshot used to build it.
/// Thread-safety: published instances are immutable. Build off the UI thread and discard if generation/query mismatch.
/// Cancellation: honor the token while scanning; do not hit the filesystem.
/// </summary>
public sealed class EntryViewIndex : IReadOnlyList<int>
{
    public const int DebounceEntryThreshold = 10_000;

    public static TimeSpan DebounceDelay { get; } = TimeSpan.FromMilliseconds(40);

    private readonly int[] _indexes;

    private EntryViewIndex(EntrySnapshot snapshot, int[] indexes, long generation, EntrySort sort, EntryFilter filter, NameSection[] sections,
        bool allowAppendProgress = false)
    {
        Snapshot = snapshot;
        _indexes = indexes;
        Generation = generation;
        Sort = sort;
        Filter = filter;
        NameSections = Array.AsReadOnly(sections);
        _allowAppendProgress = allowAppendProgress;
    }

    public EntrySnapshot Snapshot { get; }
    public long DataVersion => Snapshot.DataVersion;
    public bool IsCurrent(EntryStore store) => Snapshot.IsCurrent(store);
    private readonly bool _allowAppendProgress;

    /// <summary>Only explicitly progressive views may lag pure enumeration appends.</summary>
    public bool CanPublish(EntryStore store) => IsCurrent(store)
        || (_allowAppendProgress && Snapshot.IsAppendCompatible(store));

    /// <summary>Retains the exact sorted rows but requires exact freshness for final publication.</summary>
    public EntryViewIndex AsFinal() => _allowAppendProgress ? new(this) : this;

    private EntryViewIndex(EntryViewIndex source)
    {
        Snapshot = source.Snapshot;
        _indexes = source._indexes;
        Generation = source.Generation;
        Sort = source.Sort;
        Filter = source.Filter;
        NameSections = source.NameSections;
        _allowAppendProgress = false;
    }

    public FileEntryCore GetEntry(EntryStore store, int viewIndex)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!Snapshot.BelongsTo(store)) throw new ArgumentException("Index belongs to another store.", nameof(store));
        return Snapshot[_indexes[viewIndex]];
    }

    public long Generation { get; }

    public EntrySort Sort { get; }

    public EntryFilter Filter { get; }
    public IReadOnlyList<NameSection> NameSections { get; }

    public int Count => _indexes.Length;

    public int this[int index] => _indexes[index];

    /// <summary>Preserves the provider's global ordering for an already ranked or paged result set.</summary>
    public static EntryViewIndex InSourceOrder(EntryStore store, long generation, EntrySort? sort = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var entries = store.CaptureSnapshot();
        return new EntryViewIndex(entries, Enumerable.Range(0, entries.Count).ToArray(),
            generation, sort ?? EntrySort.Name, new EntryFilter(), []);
    }

    /// <summary>
    /// Adapts an independently ordered hierarchical projection. Copies and validates every
    /// source position, retaining this exact immutable snapshot rather than recapturing a live store.
    /// No name-based ordering, filtering, or identity assumptions are applied.
    /// </summary>
    public static EntryViewIndex FromSourceIndexes(EntrySnapshot snapshot, IReadOnlyList<int> sourceIndexes,
        long generation, EntrySort sort, EntryFilter filter, CancellationToken cancellationToken = default,
        bool allowAppendProgress = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sourceIndexes);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        cancellationToken.ThrowIfCancellationRequested();
        var indexes = new int[sourceIndexes.Count];
        // Collapsed trees can show a handful of rows from a very large snapshot.
        // Keep duplicate validation proportional to visible rows in that case.
        var sparseSeen = sourceIndexes.Count < snapshot.Count / 8 ? new HashSet<int>() : null;
        var seen = sparseSeen is null ? new bool[snapshot.Count] : null;
        for (var i = 0; i < indexes.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = sourceIndexes[i];
            if ((uint)index >= (uint)snapshot.Count || (sparseSeen is not null ? !sparseSeen.Add(index) : seen![index]))
                throw new ArgumentException("Source positions must be unique and within the captured snapshot.", nameof(sourceIndexes));
            if (seen is not null) seen[index] = true;
            indexes[i] = index;
        }
        return new EntryViewIndex(snapshot, indexes, generation, sort, filter, [], allowAppendProgress);
    }

    public static bool ShouldDebounce(int storeCount, string previousQuery, string nextQuery)
    {
        _ = previousQuery;
        if (string.IsNullOrEmpty(nextQuery))
        {
            return false;
        }

        return storeCount >= DebounceEntryThreshold;
    }

    public bool Matches(long generation, EntryFilter filter) =>
        Generation == generation && Filter.Equals(filter);

    /// <summary>
    /// Compares stable row identities in immutable snapshots without enumerator allocations. Callers must
    /// separately validate the owning store and generation before treating a prefix as an append.
    /// </summary>
    public bool HasSameSourcePrefix(EntryViewIndex previous, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Snapshot.Owner, previous.Snapshot.Owner) || previous.Count > Count) return false;
        if (ReferenceEquals(Snapshot, previous.Snapshot)
            && _indexes.AsSpan(0, previous.Count).SequenceEqual(previous._indexes)) return true;
        for (var i = 0; i < previous.Count; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (Snapshot[_indexes[i]].Id != previous.Snapshot[previous._indexes[i]].Id) return false;
        }
        return true;
    }

    /// <summary>Compute off UI; only these exact immutable index references may consume the result.</summary>
    public EntrySourcePrefixProof CompareSourcePrefix(EntryViewIndex previous, CancellationToken cancellationToken = default, bool allowRangeReplacement = false)
    {
        var samePrefix = HasSameSourcePrefix(previous, cancellationToken);
        // Expansion-only projections share an exact immutable snapshot. Prove
        // one contiguous insertion/removal on the worker, avoiding a whole
        // repeater reset or a large comparison on the input thread.
        if (!samePrefix && Generation == previous.Generation && ReferenceEquals(Snapshot, previous.Snapshot)
            && (Count != previous.Count || allowRangeReplacement) && Sort == previous.Sort && Filter == previous.Filter)
        {
            var prefix = 0;
            while (prefix < Math.Min(Count, previous.Count) && _indexes[prefix] == previous._indexes[prefix])
            { if ((prefix & 255) == 0) cancellationToken.ThrowIfCancellationRequested(); ++prefix; }
            var suffix = 0;
            while (suffix < Math.Min(Count, previous.Count) - prefix
                && _indexes[Count - 1 - suffix] == previous._indexes[previous.Count - 1 - suffix])
            { if ((suffix & 255) == 0) cancellationToken.ThrowIfCancellationRequested(); ++suffix; }
            var removed = previous.Count - prefix - suffix;
            var added = Count - prefix - suffix;
            if ((removed == 0) != (added == 0) || allowRangeReplacement && removed > 0 && added > 0)
                return new(this, previous, false, prefix, removed, added);
        }
        return new(this, previous, samePrefix);
    }

    public int IndexOfId(EntryStore store, int id)
    {
        if (!Snapshot.BelongsTo(store)) return -1;
        for (var i = 0; i < _indexes.Length; i++)
            if (Snapshot[_indexes[i]].Id == id) return i;
        return -1;
    }

    public static EntryViewIndex Build(
        EntryStore store,
        EntrySort sort,
        EntryFilter filter,
        IComparer<string> nameComparer,
        long generation,
        CancellationToken cancellationToken = default)
        => Build(store, sort, filter, nameComparer, generation, additionalMatch: null, cancellationToken);

    /// <summary>
    /// Builds an index with an optional in-memory predicate. The predicate is applied
    /// after the normal folder filter and never performs filesystem I/O.
    /// </summary>
    public static EntryViewIndex Build(
        EntryStore store,
        EntrySort sort,
        EntryFilter filter,
        IComparer<string> nameComparer,
        long generation,
        Func<FileEntryCore, bool>? additionalMatch,
        CancellationToken cancellationToken = default,
        Func<FileEntryCore, ulong>? sizeOf = null,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues = null)
        => Build(store, sort, filter, nameComparer, generation, additionalMatch,
            cancellationToken, sizeOf, propertyValues, Guid.Empty, 0);

    public static EntryViewIndex Build(
        EntryStore store, EntrySort sort, EntryFilter filter, IComparer<string> nameComparer,
        long generation, Func<FileEntryCore, bool>? additionalMatch,
        CancellationToken cancellationToken, Func<FileEntryCore, ulong>? sizeOf,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues,
        Guid diagnosticPane, long diagnosticOperation)
    {
        ArgumentNullException.ThrowIfNull(store);
        return Build(store.CaptureSnapshot(cancellationToken, diagnosticPane, generation, diagnosticOperation),
            sort, filter, nameComparer, generation, additionalMatch, cancellationToken, sizeOf, propertyValues);
    }

    /// <summary>Sorts outside the store lock, retaining the same rows used for metadata/size keys.</summary>
    public static EntryViewIndex Build(EntrySnapshot entries, EntrySort sort, EntryFilter filter,
        IComparer<string> nameComparer, long generation, Func<FileEntryCore, bool>? additionalMatch = null,
        CancellationToken cancellationToken = default, Func<FileEntryCore, ulong>? sizeOf = null,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues = null, bool allowAppendProgress = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(nameComparer);
        cancellationToken.ThrowIfCancellationRequested();
        var n = entries.Count;
        var rented = ArrayPool<int>.Shared.Rent(Math.Max(n, 1));
        try
        {
            var w = 0;
            for (var i = 0; i < n; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (filter.Matches(entries[i])
                    && (additionalMatch is null || additionalMatch(entries[i])))
                {
                    rented[w++] = i;
                }
            }

            // Sort compact matching positions, then map back to source rows. Key storage scales
            // with matches rather than the entire directory (including an empty filtered view).
            var exact = w == 0 ? [] : new int[w];
            for (var i = 0; i < w; i++) exact[i] = i;
            // Keys are computed once on this background build, never during comparisons or scrolling.
            var keys = sort.Column == EntrySortColumn.Name && w > 0 ? new string[w] : null;
            var chinese = keys is null ? null : new bool[w];
            if (keys is not null)
                for (var i = 0; i < w; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = rented[i];
                    keys[i] = PinyinName.Key(entries[row].Name);
                    chinese![i] = PinyinName.StartsWithHan(entries[row].Name);
                }
            var locations = sort.Column == EntrySortColumn.Location && w > 0 ? new string[w] : null;
            if (locations is not null)
                for (var i = 0; i < w; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    locations[i] = Path.GetDirectoryName(entries[rented[i]].Name) ?? string.Empty;
                }
            // Folder walks may finish during sorting. Sample each size once so
            // comparisons use consistent keys throughout this build.
            var sizes = sort.Column == EntrySortColumn.Size && sizeOf is not null ? new ulong[w] : null;
            if (sizes is not null)
                for (var i = 0; i < w; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = rented[i];
                    sizes[i] = sizeOf!(entries[row]);
                }
            try
            {
                var comparer = new RowComparer(entries, w == n ? null : rented, sort, nameComparer, sizes, keys, chinese, locations, propertyValues, cancellationToken);
                // Enumeration often arrives already ordered. Verify with the exact
                // production comparer before paying for a full sort; the first
                // inversion retains the existing sorting and cancellation behavior.
                var ordered = true;
                try
                {
                    for (var i = 1; i < exact.Length; i++)
                        if (comparer.Compare(exact[i - 1], exact[i]) > 0) { ordered = false; break; }
                }
                catch (Exception error) when (error is not OperationCanceledException cancelled
                    || cancelled.CancellationToken != cancellationToken || !cancellationToken.IsCancellationRequested)
                {
                    // Preserve Array.Sort's comparer-failure contract; a foreign
                    // cancellation must not masquerade as this build's cancellation.
                    throw new InvalidOperationException("Failed to compare two elements in the array.", error);
                }
                if (!ordered) Array.Sort(exact, comparer);
            }
            catch (InvalidOperationException error) when (error.InnerException is OperationCanceledException cancelled
                && cancelled.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested)
            {
                // Array.Sort wraps comparer exceptions. Preserve cancellation instead of reporting a failed build.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cancelled).Throw();
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var sections = new List<NameSection>();
            if (keys is not null)
                for (var i = 0; i < w; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = exact[i];
                    var label = PinyinName.Initial(keys[row]);
                    // A mixed list has one alphabet stream, without a file/folder chooser.
                    var directory = sort.EffectiveGrouping != EntryGrouping.Mixed && entries[rented[row]].Kind == EntryKind.Directory;
                    if (sections.Count == 0 || sections[^1].Label != label || sections[^1].IsDirectory != directory)
                        sections.Add(new(label, i, directory));
                }
            if (w != n)
                for (var i = 0; i < w; i++) exact[i] = rented[exact[i]];
            return new EntryViewIndex(entries, exact, generation, sort, filter, sections.ToArray(), allowAppendProgress);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rented);
        }
    }

    public IEnumerator<int> GetEnumerator()
    {
        foreach (var index in _indexes)
        {
            yield return index;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class RowComparer(
        EntrySnapshot entries,
        int[]? sourceRows,
        EntrySort sort,
        IComparer<string> names,
        ulong[]? sizes, string[]? keys, bool[]? chinese, string[]? locations,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues,
        CancellationToken cancellationToken) : IComparer<int>
    {
        private readonly EntryGrouping _grouping = sort.EffectiveGrouping;
        private int _comparisons;

        public int Compare(int x, int y)
        {
            // Bound obsolete sort work without paying a token check for every comparison.
            if ((_comparisons++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
            var left = entries[sourceRows is null ? x : sourceRows[x]];
            var right = entries[sourceRows is null ? y : sourceRows[y]];
            if (_grouping != EntryGrouping.Mixed && left.Kind != right.Kind)
            {
                var directoryOrder = left.Kind == EntryKind.Directory ? -1 : 1;
                return _grouping == EntryGrouping.FoldersFirst ? directoryOrder : -directoryOrder;
            }

            if (sort.Column == EntrySortColumn.ShellProperty)
            {
                var a = propertyValues?.GetValueOrDefault(left.Id);
                var b = propertyValues?.GetValueOrDefault(right.Id);
                var result = EntryPropertyValue.Compare(a, b, names);
                // Missing metadata belongs at the end in either direction.
                if ((a is null) != (b is null)) return result;
                if (result == 0) result = names.Compare(left.Name, right.Name);
                if (result == 0) result = x.CompareTo(y);
                return sort.Ascending ? result : -result;
            }

            var cmp = sort.Column switch
            {
                EntrySortColumn.Size => (sizes?[x] ?? left.Size).CompareTo(sizes?[y] ?? right.Size),
                EntrySortColumn.Modified => left.ModifiedUtcTicks.CompareTo(right.ModifiedUtcTicks),
                EntrySortColumn.Created => CompareCreated(left.CreatedUtcTicks, right.CreatedUtcTicks),
                EntrySortColumn.Accessed => CompareCreated(left.AccessedUtcTicks, right.AccessedUtcTicks),
                EntrySortColumn.Location => names.Compare(locations![x], locations[y]),
                EntrySortColumn.FullPath => names.Compare(left.Name, right.Name),
                EntrySortColumn.Type => CompareExtension(left.Name, right.Name),
                EntrySortColumn.Extension => CompareExtension(left.Name, right.Name),
                EntrySortColumn.Attributes => ((int)left.Attributes).CompareTo((int)right.Attributes),
                _ => CompareName(x, y, left.Name, right.Name),
            };

            if (cmp == 0 && sort.Column == EntrySortColumn.Size)
            {
                cmp = names.Compare(left.Name, right.Name);
            }

            if (cmp == 0)
            {
                cmp = x.CompareTo(y);
            }

            return sort.Ascending ? cmp : -cmp;
        }


        private int CompareName(int left, int right, string leftName, string rightName)
        {
            if (keys is null) return names.Compare(leftName, rightName);
            if (!sort.MixChineseAndLatin && chinese![left] != chinese[right]) return chinese[left] ? 1 : -1;
            var comparison = names.Compare(keys[left], keys[right]);
            return comparison != 0 ? comparison : names.Compare(leftName, rightName);
        }

        private static int CompareExtension(string left, string right)
        {
            var leftStart = left.LastIndexOf('.');
            var rightStart = right.LastIndexOf('.');
            var leftExt = leftStart < 0 ? ReadOnlySpan<char>.Empty : left.AsSpan(leftStart);
            var rightExt = rightStart < 0 ? ReadOnlySpan<char>.Empty : right.AsSpan(rightStart);
            return leftExt.CompareTo(rightExt, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareCreated(long left, long right)
        {
            if (left == 0 && right == 0)
            {
                return 0;
            }

            if (left == 0)
            {
                return 1;
            }

            if (right == 0)
            {
                return -1;
            }

            return left.CompareTo(right);
        }
    }
}
