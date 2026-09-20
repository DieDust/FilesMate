using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntryStoreTests
{
    [Fact]
    public void Upsert_keeps_id_when_replacing_by_name()
    {
        var store = new EntryStore();
        store.Append([File(1, "a.txt", 1)]);
        store.Upsert(File(99, "a.txt", 8));
        store.Upsert(File(0, "b.txt", 2));

        Assert.Equal(2, store.Count);
        Assert.Equal(1, store[0].Id);
        Assert.Equal(8UL, store[0].Size);
        Assert.Equal("b.txt", store[1].Name);
        Assert.Equal(2, store[1].Id);
    }

    [Fact]
    public void RemoveByName_is_case_insensitive()
    {
        var store = new EntryStore();
        store.Append([File(1, "Old.txt"), File(2, "keep.txt")]);

        Assert.True(store.RemoveByName("old.txt"));
        Assert.False(store.RemoveByName("missing.txt"));
        Assert.Equal(1, store.Count);
        Assert.Equal("keep.txt", store[0].Name);
    }

    [Fact]
    public void Rename_keeps_id_and_drops_a_colliding_destination()
    {
        var store = new EntryStore();
        store.Append([File(1, "a.txt", 1), File(2, "b.txt", 2)]);
        store.Rename("a.txt", File(99, "b.txt", 9));

        Assert.Equal(1, store.Count);
        Assert.Equal("b.txt", store[0].Name);
        Assert.Equal(1, store[0].Id);
        Assert.Equal(9UL, store[0].Size);
    }

    private static FileEntryCore File(int id, string name, ulong size = 1) =>
        new(id, name, size, ModifiedUtcTicks: 1, CreatedUtcTicks: 1, FileAttributes.Normal, EntryKind.File);
}
