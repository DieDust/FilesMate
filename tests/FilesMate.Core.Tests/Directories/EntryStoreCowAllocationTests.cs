using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntryStoreCowAllocationTests
{
    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65_536)]
    [InlineData(131_072)]
    public void Append_across_a_capacity_boundary_keeps_retained_rows_immutable_and_allocations_bounded(int count)
    {
        var store = new EntryStore();
        store.Append(Enumerable.Range(1, count).Select(Row).ToArray());
        var original = store.CaptureSnapshot();
        FileEntryCore[] extra = [Row(count + 1)];
        var before = GC.GetAllocatedBytesForCurrentThread();
        store.Append(extra);
        var appended = store.CaptureSnapshot();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 65_536, $"One-row append/capture allocated {allocated} bytes at {count} rows.");
        Assert.Equal(count, original.Count);
        Assert.Equal(Row(count), original[count - 1]);
        Assert.Equal(count + 1, appended.Count);
        Assert.Equal(extra[0], appended[count]);
        Assert.True(original.IsAppendCompatible(store));
        store.Upsert(Row(count) with { Size = 99 });
        Assert.Equal(1UL, original[count - 1].Size);
        Assert.Equal(1UL, appended[count - 1].Size);
        Assert.Equal(99UL, store.CaptureSnapshot()[count - 1].Size);
    }
    private static FileEntryCore Row(int id) => new(id, $"item{id:D6}", 1, 1, 1, FileAttributes.Normal, EntryKind.File);
}
