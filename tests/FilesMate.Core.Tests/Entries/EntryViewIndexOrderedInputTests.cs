using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Entries;

public sealed class EntryViewIndexOrderedInputTests
{
    [Fact]
    public void Ordered_filtered_input_preserves_exact_rows_with_linear_name_comparisons()
    {
        var store = OrderedStore(4096);
        var comparisons = 0;
        var names = Comparer<string>.Create((a, b) => { comparisons++; return StringComparer.Ordinal.Compare(a, b); });
        var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, names,
            7, entry => entry.Id % 2 == 0);
        Assert.Equal(Enumerable.Range(1, 4096).Where(id => id % 2 == 0), index.Select(i => store[i].Id));
        Assert.Equal(7, index.Generation);
        Assert.InRange(comparisons, 1, 4096);
        Assert.True(index.IsCurrent(store));
    }

    [Fact]
    public void Cancellation_during_ordered_verification_stops_before_publication()
    {
        var store = OrderedStore(4096);
        using var cancellation = new CancellationTokenSource();
        var comparisons = 0;
        var names = Comparer<string>.Create((a, b) =>
        {
            if (++comparisons == 100) cancellation.Cancel();
            return StringComparer.Ordinal.Compare(a, b);
        });
        var error = Assert.Throws<OperationCanceledException>(() =>
            EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, names, 9, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.InRange(comparisons, 100, 164);
    }

    private static EntryStore OrderedStore(int count)
    {
        var store = new EntryStore();
        store.Append(Enumerable.Range(1, count).Select(id => new FileEntryCore(id,
            $"entry-{id:D6}.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File)).ToArray());
        return store;
    }
}
