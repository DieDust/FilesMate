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

    [Fact]
    public void Reconcile_preserves_survivors_and_reports_only_changed_paths()
    {
        var store = new EntryStore();
        store.Append([File(20, "keep.txt"), File(30, "changed.txt"), File(40, "deleted.txt")]);
        var changed = store.Reconcile([File(1, "changed.txt", 99), File(2, "keep.txt"), File(3, "created.txt")]);
        Assert.Equal(new[] { "changed.txt", "created.txt", "deleted.txt" }, changed.Order(StringComparer.Ordinal));
        Assert.Equal(20, store.Snapshot().Single(e => e.Name == "keep.txt").Id);
        Assert.Equal(30, store.Snapshot().Single(e => e.Name == "changed.txt").Id);
        Assert.Equal(41, store.Snapshot().Single(e => e.Name == "created.txt").Id);
        Assert.Equal(99UL, store.Snapshot().Single(e => e.Name == "changed.txt").Size);
    }

    [Fact]
    public void Reconcile_of_reordered_unchanged_listing_keeps_existing_index_positions()
    {
        var store = new EntryStore();
        store.Append([File(10, "b.txt"), File(20, "a.txt")]);
        Assert.Empty(store.Reconcile([File(1, "a.txt"), File(2, "b.txt")]));
        Assert.Equal(new[] { "b.txt", "a.txt" }, store.Snapshot().Select(e => e.Name));
    }
}
