using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntrySnapshotTests
{
    [Fact]
    public void Every_row_mutation_invalidates_the_cached_version_and_keeps_old_rows_immutable()
    {
        var store = new EntryStore();
        store.Append([Row(1, "a"), Row(2, "b")]);
        var first = store.CaptureSnapshot();
        Assert.Same(first, store.CaptureSnapshot());
        Assert.True(first.IsCurrent(store));
        void Mutate(Action action)
        {
            var before = store.CaptureSnapshot();
            action();
            var after = store.CaptureSnapshot();
            Assert.False(before.IsCurrent(store));
            Assert.True(after.IsCurrent(store));
            Assert.True(after.DataVersion > before.DataVersion);
        }
        Mutate(() => store.Append([Row(3, "c")]));
        Mutate(() => store.Upsert(Row(99, "a") with { Size = 7 }));
        Mutate(() => store.Rename("b", Row(99, "d")));
        Mutate(() => store.Rename("d", Row(99, "a"))); // Overwrite a destination, preserving source ID.
        Mutate(() => store.Reconcile([Row(0, "a") with { Size = 8 }, Row(0, "new")]));
        Mutate(() => Assert.True(store.RemoveByName("new")));
        Assert.Equal([(1, "a", 1UL), (2, "b", 1UL)], first.Select(e => (e.Id, e.Name, e.Size)));
        Assert.Equal(2, store.CaptureSnapshot()[0].Id);
        var detached = store.Snapshot();
        detached[0] = Row(100, "external");
        Assert.Equal("a", store.CaptureSnapshot()[0].Name);
    }

    [Fact]
    public void Noop_mutations_reuse_the_same_snapshot_and_version()
    {
        var store = new EntryStore();
        store.Append([Row(1, "a"), Row(2, "b")]);
        var snapshot = store.CaptureSnapshot();
        store.Append([]);
        Assert.False(store.RemoveByName("missing"));
        store.Upsert(Row(99, "a"));
        store.Rename("a", Row(99, "a"));
        Assert.Empty(store.Reconcile([Row(99, "b"), Row(99, "a")]));
        Assert.Same(snapshot, store.CaptureSnapshot());
    }

    [Fact]
    public void Size_sampling_can_mutate_the_live_store_without_changing_the_sort_snapshot()
    {
        var store = new EntryStore();
        store.Append([Row(1, "a"), Row(2, "b"), Row(3, "c")]);
        var snapshot = store.CaptureSnapshot();
        var reads = new Dictionary<int, int>();
        var index = EntryViewIndex.Build(snapshot, EntrySort.Size, EntryFilter.None,
            NaturalStringComparer.Instance, 5, sizeOf: entry =>
            {
                reads[entry.Id] = reads.GetValueOrDefault(entry.Id) + 1;
                if (entry.Id == 1) store.Reconcile([Row(0, "c"), Row(0, "new")]);
                return (ulong)(4 - entry.Id);
            });
        Assert.All(reads.Values, value => Assert.Equal(1, value));
        Assert.Equal(["c", "b", "a"], Enumerable.Range(0, index.Count).Select(i => index.GetEntry(store, i).Name));
        Assert.Same(snapshot, index.Snapshot);
        Assert.False(index.IsCurrent(store));
        Assert.Equal(5, index.Generation);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.CaptureSnapshot(cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => EntryViewIndex.Build(snapshot, EntrySort.Name,
            EntryFilter.None, NaturalStringComparer.Instance, 5, cancellationToken: cancelled.Token));
    }

    private static FileEntryCore Row(int id, string name) => new(id, name, 1, 0, 0, FileAttributes.Normal, EntryKind.File);

    [Fact]
    public void Hundred_thousand_rows_share_immutable_blocks_and_cached_reads_allocate_nothing()
    {
        var store = new EntryStore();
        store.Append(Enumerable.Range(1, 100_000).Select(i => Row(i, $"item{i}")).ToArray());
        var before = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = store.CaptureSnapshot();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 16_384);
        Assert.Equal(store.Snapshot(), snapshot.ToArray());
        for (var i = 0; i < 3; i++) store.CaptureSnapshot();
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) Assert.Same(snapshot, store.CaptureSnapshot());
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
