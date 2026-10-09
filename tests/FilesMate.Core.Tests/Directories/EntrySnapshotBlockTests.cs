using System.Collections;
using System.Runtime.CompilerServices;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntrySnapshotBlockTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(255)] [InlineData(256)] [InlineData(257)]
    [InlineData(511)] [InlineData(512)] [InlineData(513)] [InlineData(1023)] [InlineData(1024)]
    [InlineData(1025)] [InlineData(2047)] [InlineData(2048)]
    public void Logical_length_and_both_enumerators_are_exact(int count)
    {
        var store = Make(count); var snapshot = store.CaptureSnapshot();
        Assert.Equal(count, snapshot.Count);
        Assert.Throws<IndexOutOfRangeException>(() => snapshot[-1]);
        Assert.Throws<IndexOutOfRangeException>(() => snapshot[count]);
        if (count > 0) Assert.Equal(count, snapshot[count - 1].Id);
        Assert.Equal(store.Snapshot(), snapshot.ToArray());
        Assert.Equal(store.Snapshot(), ((IEnumerable)snapshot).Cast<FileEntryCore>().ToArray());
    }

    [Fact]
    public void Enumerator_current_reset_and_end_are_consistent()
    {
        var snapshot = Make(257).CaptureSnapshot();
        using var iterator = ((IEnumerable<FileEntryCore>)snapshot).GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => iterator.Current);
        for (var i = 0; i < snapshot.Count; i++) { Assert.True(iterator.MoveNext()); Assert.Equal(i + 1, iterator.Current.Id); }
        Assert.False(iterator.MoveNext()); Assert.False(iterator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => iterator.Current);
        iterator.Reset(); Assert.Throws<InvalidOperationException>(() => iterator.Current);
        Assert.True(iterator.MoveNext()); Assert.Equal(1, iterator.Current.Id);
    }

    [Fact]
    public void Direct_foreach_has_no_enumerator_allocation()
    {
        var snapshot = Make(2049).CaptureSnapshot();
        long sum = 0;
        for (var i = 0; i < 3; i++) foreach (var row in snapshot) sum += row.Id;
        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var row in snapshot) sum += row.Id;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(4L * 2049 * 2050 / 2, sum);
    }

    [Fact]
    public void Dirty_blocks_union_across_mutations_and_old_versions_never_change()
    {
        var store = Make(4097); var first = store.CaptureSnapshot(); var original = first.ToArray();
        store.Upsert(Row(2) with { Size = 111 });
        store.Upsert(Row(3073) with { Size = 222 });
        store.Rename("item2048", Row(9999) with { Name = "renamed" });
        store.Append([Row(4098), Row(4099)]);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => store.CaptureSnapshot(cancellation.Token));
        }
        var second = store.CaptureSnapshot(); var secondRows = second.ToArray();
        Assert.Equal(111UL, second[1].Size); Assert.Equal(222UL, second[3072].Size);
        Assert.Equal((2048, "renamed"), (second[2047].Id, second[2047].Name));
        Assert.Equal(store.Snapshot(), secondRows);
        store.RemoveByName("item1024"); store.RemoveByName("item1"); store.RemoveByName("item4099");
        var third = store.CaptureSnapshot(); Assert.Equal(store.Snapshot(), third.ToArray());
        store.Rename("item1025", Row(0) with { Name = "item4098" }); // Remove destination and shift.
        Assert.Equal(store.Snapshot(), store.CaptureSnapshot().ToArray());
        store.Reconcile([Row(1), Row(4), Row(2)]);
        Assert.Equal(store.Snapshot(), store.CaptureSnapshot().ToArray());
        store.Append(Enumerable.Range(10, 2050).Select(Row).ToArray());
        Assert.Equal(store.Snapshot(), store.CaptureSnapshot().ToArray());
        Assert.Equal(original, first.ToArray()); Assert.Equal(secondRows, second.ToArray());
    }

    [Fact]
    public void Random_mutation_sequence_matches_live_store_and_retained_snapshots()
    {
        var random = new Random(20261002); var store = Make(3100);
        var retained = new List<(EntrySnapshot Snapshot, FileEntryCore[] Rows)>();
        for (var step = 0; step < 200; step++)
        {
            var live = store.Snapshot(); var position = random.Next(live.Length);
            switch (step % 6)
            {
                case 0: store.Upsert(live[position] with { Size = (ulong)step + 500 }); break;
                case 1: store.Append([Row(4000 + step)]); break;
                case 2: store.RemoveByName(live[position].Name); break;
                case 3: store.Rename(live[position].Name, Row(6000 + step)); break;
                case 4: store.Rename(live[position].Name, live[random.Next(live.Length)]); break;
                case 5:
                    random.Shuffle(live); live[0] = live[0] with { Size = (ulong)step + 700 };
                    store.Reconcile(live); break;
            }
            if (step % 3 != 0) continue;
            var snapshot = store.CaptureSnapshot(); var rows = store.Snapshot();
            Assert.Equal(rows, snapshot.ToArray()); retained.Add((snapshot, rows));
        }
        foreach (var pair in retained) Assert.Equal(pair.Rows, pair.Snapshot.ToArray());
    }

    [Fact]
    public void Sparse_100k_changes_and_appends_have_bounded_copy_allocations()
    {
        var store = Make(100_000); store.CaptureSnapshot();
        // Warm mutation tracking; measure allocation for Capture only, excluding mutation lookup.
        store.Upsert(Row(2) with { Size = 2 }); store.CaptureSnapshot();
        for (var i = 0; i < 16; i++) store.Upsert(Row(1 + i * 6000) with { Size = 2 });
        var before = GC.GetAllocatedBytesForCurrentThread(); var changed = store.CaptureSnapshot();
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 1, 300_000);
        for (var i = 0; i < 16; i++) store.Upsert(Row(2049 + i) with { Size = 2 });
        before = GC.GetAllocatedBytesForCurrentThread(); store.CaptureSnapshot();
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 1, 20_000);
        store.Append([Row(100_001)]);
        before = GC.GetAllocatedBytesForCurrentThread(); var appended = store.CaptureSnapshot();
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 1, 20_000);
        Assert.Equal(100_000, changed.Count); Assert.Equal(100_001, appended.Count);
    }

    [Fact]
    public void Seventy_two_progressive_enumeration_snapshots_keep_capture_allocation_below_half_megabyte()
    {
        var store = new EntryStore(); long allocated = 0; var count = 0;
        for (var batch = 1; batch <= 72; batch++)
        {
            var target = 100_000 * batch / 72;
            store.Append(Enumerable.Range(count + 1, target - count).Select(Row).ToArray());
            count = target;
            var before = GC.GetAllocatedBytesForCurrentThread(); var snapshot = store.CaptureSnapshot();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(count, snapshot.Count); Assert.True(snapshot.IsCurrent(store));
        }
        Assert.InRange(allocated, 1, 512_000);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_the_gate_keeps_dirty_rows_for_the_next_capture()
    {
        var store = Make(4097); var original = store.CaptureSnapshot();
        store.Upsert(Row(1) with { Size = 500 }); store.Upsert(Row(3073) with { Size = 600 });
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var attempted = new ManualResetEventSlim(); using var cancellation = new CancellationTokenSource();
        var holder = Task.Factory.StartNew(() => store.Observe(rows =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Gate was not released.");
            return rows.Count;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var capture = Task.Factory.StartNew(() =>
        {
            attempted.Set(); return store.CaptureSnapshot(cancellation.Token);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(attempted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(capture.IsCompleted);
            cancellation.Cancel();
        }
        finally { release.Set(); await holder; }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await capture);
        var current = store.CaptureSnapshot();
        Assert.Equal(store.Snapshot(), current.ToArray());
        Assert.Equal(500UL, current[0].Size); Assert.Equal(600UL, current[3072].Size);
        Assert.Equal(1UL, original[0].Size); Assert.Equal(1UL, original[3072].Size);
    }

    [Fact]
    public void Snapshot_reuse_does_not_keep_a_chain_of_prior_snapshot_objects()
    {
        var store = Make(100_000); var references = MutateAndCapture(store);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.Equal(1, references.Count(reference => reference.IsAlive));
        GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] MutateAndCapture(EntryStore store)
    {
        var references = new WeakReference[50];
        for (var i = 0; i < references.Length; i++)
        {
            store.Upsert(Row(1) with { Size = (ulong)i + 500 });
            references[i] = new WeakReference(store.CaptureSnapshot());
        }
        return references;
    }
    private static EntryStore Make(int count) { var store = new EntryStore(); store.Append(Enumerable.Range(1, count).Select(Row).ToArray()); return store; }
    private static FileEntryCore Row(int id) => new(id, $"item{id}", 1, 0, 0, FileAttributes.Normal, EntryKind.File);
}
