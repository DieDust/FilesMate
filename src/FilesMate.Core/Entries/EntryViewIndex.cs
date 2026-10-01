using System.Buffers;
using System.Collections;

using FilesMate.Core.Directories;

namespace FilesMate.Core.Entries;

public sealed record NameSection(string Label, int FirstIndex, bool IsDirectory);

/// <summary>
/// Immutable integer index into an <see cref="EntryStore"/>. Sorting never copies entry structs.
/// Thread-safety: published instances are immutable. Build off the UI thread and discard if generation/query mismatch.
/// Cancellation: honor the token while scanning; do not hit the filesystem.
/// </summary>
public sealed class EntryViewIndex : IReadOnlyList<int>
{
    public const int DebounceEntryThreshold = 10_000;

    public static TimeSpan DebounceDelay { get; } = TimeSpan.FromMilliseconds(40);

    private readonly int[] _indexes;

    private EntryViewIndex(int[] indexes, long generation, EntrySort sort, EntryFilter filter, NameSection[] sections)
    {
        _indexes = indexes;
        Generation = generation;
        Sort = sort;
        Filter = filter;
        NameSections = Array.AsReadOnly(sections);
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
        return store.Observe(entries => new EntryViewIndex(Enumerable.Range(0, entries.Count).ToArray(),
            generation, sort ?? EntrySort.Name, new EntryFilter(), []));
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

    public int IndexOfId(EntryStore store, int id)
    {
        return store.Observe(entries =>
        {
            for (var i = 0; i < _indexes.Length; i++)
            {
                if ((uint)_indexes[i] < (uint)entries.Count && entries[_indexes[i]].Id == id) return i;
            }
            return -1;
        });
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
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(nameComparer);

        return store.Observe(entries =>
        {
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

                // Keys are computed once on this background build, never during comparisons or scrolling.
                var keys = sort.Column == EntrySortColumn.Name ? new string[n] : null;
                var chinese = keys is null ? null : new bool[n];
                if (keys is not null)
                    for (var i = 0; i < w; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var row = rented[i];
                        keys[row] = PinyinName.Key(entries[row].Name);
                        chinese![row] = PinyinName.StartsWithHan(entries[row].Name);
                    }
                // Folder walks may finish during sorting. Sample each size once so
                // comparisons use consistent keys throughout this build.
                var sizes = sort.Column == EntrySortColumn.Size && sizeOf is not null ? new ulong[n] : null;
                if (sizes is not null)
                    for (var i = 0; i < w; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var row = rented[i];
                        sizes[row] = sizeOf!(entries[row]);
                    }
                Array.Sort(rented, 0, w, new RowComparer(entries, sort, nameComparer, sizes, keys, chinese, propertyValues));
                var exact = w == 0 ? [] : new int[w];
                if (w > 0)
                {
                    Array.Copy(rented, exact, w);
                }

                var sections = new List<NameSection>();
                if (keys is not null)
                    for (var i = 0; i < w; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var row = exact[i];
                        var label = PinyinName.Initial(keys[row]);
                        // A mixed list has one alphabet stream, without a file/folder chooser.
                        var directory = sort.EffectiveGrouping != EntryGrouping.Mixed && entries[row].Kind == EntryKind.Directory;
                        if (sections.Count == 0 || sections[^1].Label != label || sections[^1].IsDirectory != directory)
                            sections.Add(new(label, i, directory));
                    }
                return new EntryViewIndex(exact, generation, sort, filter, sections.ToArray());
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        });
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
        IReadOnlyList<FileEntryCore> entries,
        EntrySort sort,
        IComparer<string> names,
        ulong[]? sizes, string[]? keys, bool[]? chinese,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues) : IComparer<int>
    {
        private readonly EntryGrouping _grouping = sort.EffectiveGrouping;

        public int Compare(int x, int y)
        {
            var left = entries[x];
            var right = entries[y];
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
                EntrySortColumn.Location => names.Compare(Path.GetDirectoryName(left.Name) ?? "", Path.GetDirectoryName(right.Name) ?? ""),
                EntrySortColumn.FullPath => names.Compare(left.Name, right.Name),
                EntrySortColumn.Type => CompareExtension(left.Name, right.Name),
                EntrySortColumn.Extension => CompareExtension(left.Name, right.Name),
                EntrySortColumn.Attributes => ((int)left.Attributes).CompareTo((int)right.Attributes),
                _ => CompareName(x, y),
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


        private int CompareName(int left, int right)
        {
            if (keys is null) return names.Compare(entries[left].Name, entries[right].Name);
            if (!sort.MixChineseAndLatin && chinese![left] != chinese[right]) return chinese[left] ? 1 : -1;
            var comparison = names.Compare(keys[left], keys[right]);
            return comparison != 0 ? comparison : names.Compare(entries[left].Name, entries[right].Name);
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
