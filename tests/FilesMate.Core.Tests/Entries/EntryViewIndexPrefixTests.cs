using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Entries;

public sealed class EntryViewIndexPrefixTests
{
    [Fact]
    public void Source_prefix_compares_positions_and_handles_empty_shorter_and_noncontiguous_views()
    {
        var store = new EntryStore();
        var empty = EntryViewIndex.InSourceOrder(store, 1);
        store.Append([Row(1), Row(2)]);
        var prefix = EntryViewIndex.InSourceOrder(store, 1);
        store.Append([Row(3)]);
        var full = EntryViewIndex.InSourceOrder(store, 1);
        var equalPositionsDifferentGeneration = EntryViewIndex.InSourceOrder(store, 2);
        var noncontiguous = EntryViewIndex.Build(store, EntrySort.Modified, EntryFilter.None,
            NaturalStringComparer.Instance, 1, entry => entry.Id != 2);
        var reversed = EntryViewIndex.Build(store, EntrySort.Modified with { Ascending = false },
            EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.True(full.HasSameSourcePrefix(prefix));
        Assert.True(full.HasSameSourcePrefix(equalPositionsDifferentGeneration));
        Assert.True(full.HasSameSourcePrefix(empty));
        Assert.True(empty.HasSameSourcePrefix(empty));
        Assert.False(prefix.HasSameSourcePrefix(full));
        Assert.False(empty.HasSameSourcePrefix(prefix));
        Assert.False(full.HasSameSourcePrefix(noncontiguous));
        Assert.False(full.HasSameSourcePrefix(reversed));
        Assert.Throws<ArgumentNullException>(() => full.HasSameSourcePrefix(null!));
    }

    [Fact]
    public void Equal_snapshot_position_fast_path_keeps_the_original_id_equivalence_contract()
    {
        var store = new EntryStore();
        store.Append([Row(1) with { Name = "b" }, Row(1) with { Name = "a" }]);
        var original = EntryViewIndex.InSourceOrder(store, 1);
        var sorted = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
        Assert.Same(original.Snapshot, sorted.Snapshot);
        Assert.Equal([0, 1], original); Assert.Equal([1, 0], sorted);
        Assert.True(sorted.HasSameSourcePrefix(original));
    }

    [Fact]
    public void Final_wrapper_keeps_exact_shared_snapshot_and_order_but_not_progressive_freshness()
    {
        var store = new EntryStore();
        store.Append([Row(1), Row(2)]);
        var progressive = EntryViewIndex.Build(store.CaptureSnapshot(), EntrySort.Modified, EntryFilter.None,
            NaturalStringComparer.Instance, 7, allowAppendProgress: true);
        var final = progressive.AsFinal();
        Assert.NotSame(progressive, final);
        Assert.Same(progressive.Snapshot, final.Snapshot);
        Assert.True(final.HasSameSourcePrefix(progressive));
        Assert.True(progressive.HasSameSourcePrefix(final));
        Assert.True(final.HasSameSourcePrefix(final));
        store.Append([Row(3)]);
        Assert.True(progressive.CanPublish(store));
        Assert.False(final.CanPublish(store));
    }

    private static FileEntryCore Row(int id) => new(id, $"item{id}", (ulong)id, id, id, FileAttributes.Normal, EntryKind.File);
}
