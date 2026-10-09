using System.Runtime.CompilerServices;
using Xunit.Abstractions;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntryStoreBatchAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(37)]
    [InlineData(256)]
    [InlineData(65_536)]
    [InlineData(205_005)]
    public void Known_batch_rows_and_snapshot_fit_one_row_copy_plus_small_block_tables(int count)
    {
        FileEntryCore[] rows = Enumerable.Range(1, count).Select(Row).ToArray();
        var warm = new EntryStore(); warm.Append([Row(1)]); _ = warm.CaptureSnapshot();
        var store = new EntryStore();
        var before = GC.GetAllocatedBytesForCurrentThread();
        store.Append(rows);
        var snapshot = store.CaptureSnapshot();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var bound = (long)count * Unsafe.SizeOf<FileEntryCore>() + 65_536;
        output.WriteLine($"Known batch count={count}, allocated={allocated}, bound={bound} bytes.");
        Assert.True(allocated <= bound, $"Known {count}-row batch/capture allocated {allocated}, bound {bound}.");
        Assert.Equal(count, store.Count);
        Assert.Equal(count, snapshot.Count);
        Assert.Equal(rows, snapshot.ToArray());
        store.Upsert(rows[^1] with { Size = 99 });
        Assert.Equal(1UL, snapshot[count - 1].Size);
        Assert.Equal(99UL, store.CaptureSnapshot()[count - 1].Size);
    }
    private static FileEntryCore Row(int id) => new(id, $"item{id:D6}", 1, 1, 1, FileAttributes.Normal, EntryKind.File);
}
