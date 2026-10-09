using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Directories;

public sealed class EntryAppendCompatibilityTests
{
    [Fact]
    public void Append_compatibility_is_distinct_from_exact_freshness_and_requires_the_same_store()
    {
        var store = CreateStore();
        var snapshot = store.CaptureSnapshot();
        var progressive = Build(snapshot, progressive: true);
        var exact = Build(snapshot, progressive: false);
        for (var id = 3; id < 520; id++) store.Append([Row(id, $"n{id}")]);
        Assert.False(snapshot.IsCurrent(store));
        Assert.True(snapshot.IsAppendCompatible(store));
        Assert.True(progressive.CanPublish(store));
        Assert.False(exact.CanPublish(store));
        Assert.False(progressive.IsCurrent(store));
        Assert.False(snapshot.IsAppendCompatible(CreateStore()));
        Assert.False(progressive.CanPublish(CreateStore()));
        Assert.Equal(["a", "z"], progressive.Select(i => snapshot[i].Name));
        Assert.Equal(2, snapshot.Count);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("update")]
    [InlineData("insert")]
    [InlineData("rename")]
    [InlineData("rename-overwrite")]
    [InlineData("rename-missing")]
    [InlineData("reconcile")]
    public void Every_effective_non_enumeration_mutation_permanently_revokes_old_prefixes(string operation)
    {
        var store = CreateStore();
        var snapshot = store.CaptureSnapshot();
        var index = Build(snapshot, progressive: true);
        store.Append([Row(3, "later")]);
        switch (operation)
        {
            case "remove": Assert.True(store.RemoveByName("a")); break;
            case "update": store.Upsert(Row(99, "a") with { Size = 42 }); break;
            case "insert": store.Upsert(Row(99, "inserted")); break;
            case "rename": store.Rename("a", Row(99, "renamed")); break;
            case "rename-overwrite": store.Rename("a", Row(99, "z")); break;
            case "rename-missing": store.Rename("missing", Row(99, "inserted")); break;
            case "reconcile": store.Reconcile([Row(99, "a"), Row(99, "z"), Row(99, "different")]); break;
        }
        Assert.False(snapshot.IsAppendCompatible(store));
        Assert.False(index.CanPublish(store));
        store.Append([Row(500, "appended-after-mutation")]);
        Assert.False(snapshot.IsAppendCompatible(store));
        Assert.False(index.CanPublish(store));
        var fresh = store.CaptureSnapshot();
        Assert.True(fresh.IsAppendCompatible(store));
        Assert.True(fresh.IsCurrent(store));
        Assert.Equal(["z", "a"], snapshot.Select(e => e.Name));
    }

    [Fact]
    public void Noop_mutations_keep_compatible_lineage_without_reallocating_the_snapshot()
    {
        var store = CreateStore();
        var snapshot = store.CaptureSnapshot();
        store.Append([]);
        Assert.False(store.RemoveByName("missing"));
        store.Upsert(Row(99, "a"));
        store.Rename("z", Row(99, "z"));
        Assert.Empty(store.Reconcile([Row(99, "a"), Row(99, "z")]));
        Assert.True(snapshot.IsAppendCompatible(store));
        Assert.True(snapshot.IsCurrent(store));
        Assert.Same(snapshot, store.CaptureSnapshot());
    }

    [Fact]
    public void Final_view_shares_sorted_rows_but_no_longer_accepts_later_append()
    {
        var store = CreateStore();
        var progress = Build(store.CaptureSnapshot(), progressive: true);
        var final = progress.AsFinal();
        Assert.Same(progress.Snapshot, final.Snapshot);
        Assert.Same(progress.NameSections, final.NameSections);
        Assert.Equal(progress.ToArray(), final.ToArray());
        Assert.Same(final, final.AsFinal());
        Assert.True(final.CanPublish(store));
        store.Append([Row(3, "later")]);
        Assert.True(progress.CanPublish(store));
        Assert.False(final.CanPublish(store));
    }

    private static EntryViewIndex Build(EntrySnapshot snapshot, bool progressive) =>
        EntryViewIndex.Build(snapshot, EntrySort.Name, EntryFilter.None,
            NaturalStringComparer.Instance, 1, allowAppendProgress: progressive);

    private static EntryStore CreateStore()
    {
        var store = new EntryStore();
        store.Append([Row(1, "z"), Row(2, "a")]);
        return store;
    }

    private static FileEntryCore Row(int id, string name) =>
        new(id, name, 1, 1, 1, FileAttributes.Normal, EntryKind.File);
}
