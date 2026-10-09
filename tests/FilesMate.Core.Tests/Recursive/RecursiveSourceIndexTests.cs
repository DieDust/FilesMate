using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveSourceIndexTests
{
    [Fact]
    public void Source_index_factory_copies_input_order_without_filtering_or_resorting()
    {
        var store = new EntryStore();
        store.Append([File("z.txt", id: 11), File("a.txt", id: 12), File("m.txt", id: 13)]);
        var snapshot = store.CaptureSnapshot();
        var positions = new[] { 2, 0 };
        var filter = new EntryFilter { Query = "not-present" };
        var index = EntryViewIndex.FromSourceIndexes(snapshot, positions, 4, EntrySort.Name, filter);
        positions[0] = 1;
        Assert.Equal(new[] { 2, 0 }, index.ToArray());
        Assert.Same(snapshot, index.Snapshot);
        Assert.Equal(4, index.Generation);
        Assert.Same(filter, index.Filter);
        Assert.Empty(index.NameSections);
        Assert.Equal("m.txt", index.GetEntry(store, 0).Name);
        Assert.Equal(0, index.IndexOfId(store, 13));
        Assert.Equal(-1, index.IndexOfId(store, 12));
    }

    [Fact]
    public void Retained_source_index_reads_frozen_rows_after_owner_appends()
    {
        var store = new EntryStore();
        store.Append([File("kept.txt", id: 21)]);
        var snapshot = store.CaptureSnapshot();
        var final = EntryViewIndex.FromSourceIndexes(snapshot, [0], 4, EntrySort.Name, EntryFilter.None);
        var progressive = EntryViewIndex.FromSourceIndexes(snapshot, [0], 4, EntrySort.Name,
            EntryFilter.None, allowAppendProgress: true);
        store.Append([File("later.txt", id: 22)]);
        Assert.Single(snapshot);
        Assert.Single(progressive);
        Assert.Equal("kept.txt", progressive.GetEntry(store, 0).Name);
        Assert.False(final.CanPublish(store));
        Assert.True(progressive.CanPublish(store));
        Assert.False(progressive.AsFinal().CanPublish(store));
        store.Rename("kept.txt", File("renamed.txt", id: 21));
        Assert.Equal("kept.txt", progressive.GetEntry(store, 0).Name);
        Assert.False(progressive.CanPublish(store));
    }

    [Fact]
    public void Source_index_does_not_accept_another_stores_rows_or_publication_authority()
    {
        var owner = new EntryStore();
        owner.Append([File("a.txt", id: 1)]);
        var other = new EntryStore();
        other.Append([File("a.txt", id: 1)]);
        var index = EntryViewIndex.FromSourceIndexes(owner.CaptureSnapshot(), [0], 0, EntrySort.Name, EntryFilter.None);
        Assert.False(index.CanPublish(other));
        Assert.Equal(-1, index.IndexOfId(other, 1));
        Assert.Throws<ArgumentException>(() => index.GetEntry(other, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Source_index_rejects_positions_outside_the_captured_snapshot(int position)
    {
        var store = new EntryStore();
        store.Append([File("a.txt"), File("b.txt")]);
        Assert.Throws<ArgumentException>(() => EntryViewIndex.FromSourceIndexes(store.CaptureSnapshot(),
            [position], 0, EntrySort.Name, EntryFilter.None));
    }

    [Fact]
    public void Source_index_rejects_duplicate_source_positions()
    {
        var store = new EntryStore();
        store.Append([File("a.txt"), File("b.txt")]);
        Assert.Throws<ArgumentException>(() => EntryViewIndex.FromSourceIndexes(store.CaptureSnapshot(),
            [1, 0, 1], 0, EntrySort.Name, EntryFilter.None));
    }

    [Fact]
    public void Source_index_accepts_empty_subset_and_rejects_invalid_arguments_and_cancellation()
    {
        var snapshot = new EntryStore().CaptureSnapshot();
        Assert.Empty(EntryViewIndex.FromSourceIndexes(snapshot, [], 0, EntrySort.Name, EntryFilter.None));
        Assert.Throws<ArgumentNullException>(() => EntryViewIndex.FromSourceIndexes(null!, [], 0, EntrySort.Name, EntryFilter.None));
        Assert.Throws<ArgumentNullException>(() => EntryViewIndex.FromSourceIndexes(snapshot, null!, 0, EntrySort.Name, EntryFilter.None));
        Assert.Throws<ArgumentNullException>(() => EntryViewIndex.FromSourceIndexes(snapshot, [], 0, null!, EntryFilter.None));
        Assert.Throws<ArgumentNullException>(() => EntryViewIndex.FromSourceIndexes(snapshot, [], 0, EntrySort.Name, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => EntryViewIndex.FromSourceIndexes(snapshot, [], -1, EntrySort.Name, EntryFilter.None));
        Assert.Throws<OperationCanceledException>(() => EntryViewIndex.FromSourceIndexes(snapshot,
            [], 0, EntrySort.Name, EntryFilter.None, new CancellationToken(true)));
    }
}
