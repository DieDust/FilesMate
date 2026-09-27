using FilesMate.App.Services;
using FilesMate.Search;

namespace FilesMate.App.Tests.Services;

public sealed class FavoritesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "favorites.json");

    [Fact]
    public async Task Quick_save_persists_immediately_and_reuses_the_bookmark_after_moving_it()
    {
        var store = new FavoritesStore(StorePath);
        var folder = Path.Combine(_directory, "Documents");
        var saved = await store.SaveFolderAsync(folder + Path.DirectorySeparatorChar);
        Assert.Equal(saved, new FavoritesStore(StorePath).Entries.Single());
        Assert.False(Directory.Exists(folder));
        await store.CreateGroupAsync("工作");
        var group = store.Entries.Single(entry => entry.IsGroup);
        await store.RenameAsync(saved.Id, "我的文档");
        await store.MoveAsync(saved.Id, group.Id);

        var reopened = await store.SaveFolderAsync(folder.ToUpperInvariant());
        Assert.Equal(saved.Id, reopened.Id);
        Assert.Equal(group.Id, reopened.GroupId);
        Assert.Equal("我的文档", reopened.Name);
        Assert.Equal(2, new FavoritesStore(StorePath).Entries.Count);
    }

    [Fact]
    public async Task Concurrent_quick_saves_return_one_identity_and_invalid_paths_do_not_save()
    {
        var store = new FavoritesStore(StorePath);
        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => store.SaveFolderAsync(Path.Combine(_directory, "folder"))));
        Assert.Single(results.Select(entry => entry.Id).Distinct());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFolderAsync("relative"));
        Assert.Single(new FavoritesStore(StorePath).Entries);
    }

    [Fact]
    public async Task Folder_lookup_finds_grouped_bookmarks_and_reopening_does_not_rewrite_them()
    {
        var store = new FavoritesStore(StorePath);
        var folder = Path.Combine(_directory, "Docs");
        await store.CreateGroupAsync("Work");
        var group = store.Entries.Single();
        await store.AddAsync([(folder, true)], group.Id);
        var saved = Assert.Single(store.FindFolders(folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
        var notifications = 0;
        store.Changed += (_, _) => notifications++;
        var snapshot = store.Entries;
        Assert.Equal(saved, await store.SaveFolderAsync(Path.Combine(folder, ".")));
        Assert.Same(snapshot, store.Entries);
        Assert.Equal(0, notifications);
        Assert.Empty(store.FindFolders(null));
        Assert.Empty(store.FindFolders("home:"));
        Assert.Empty(store.FindFolders("relative"));
        Assert.Empty(store.FindFolders(folder + "Other"));
    }

    [Fact]
    public async Task Editing_name_and_group_is_atomic_and_persists_without_changing_the_folder()
    {
        var store = new FavoritesStore(StorePath);
        var folder = Path.Combine(_directory, "Docs");
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "keep.txt");
        File.WriteAllText(original, "keep");
        var saved = await store.SaveFolderAsync(folder);
        await store.CreateGroupAsync("Work");
        var group = store.Entries.Single(entry => entry.IsGroup);
        await store.UpdateAsync(saved.Id, "My documents", group.Id);
        var reloaded = Assert.Single(new FavoritesStore(StorePath).FindFolders(folder));
        Assert.Equal("My documents", reloaded.Name);
        Assert.Equal(group.Id, reloaded.GroupId);
        var before = File.ReadAllText(StorePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(saved.Id, " ", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(saved.Id, "New name", "missing-group"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync("missing-id", "New name", null));
        Assert.Equal(before, File.ReadAllText(StorePath));
        await store.AddAsync([(folder, true)]);
        before = File.ReadAllText(StorePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(saved.Id, "Conflict", null));
        Assert.Equal(before, File.ReadAllText(StorePath));
        Assert.Equal("keep", File.ReadAllText(original));
    }

    [Fact]
    public async Task Removing_a_folder_clears_all_group_references_but_keeps_files_and_other_bookmarks()
    {
        var store = new FavoritesStore(StorePath);
        var folder = Path.Combine(_directory, "Docs");
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "keep.txt");
        File.WriteAllText(original, "keep");
        await store.SaveFolderAsync(folder);
        await store.CreateGroupAsync("Work");
        var group = store.Entries.Single(entry => entry.IsGroup);
        await store.AddAsync([(folder, true), (original, false)], group.Id);
        Assert.Equal(2, store.FindFolders(folder).Count);
        await store.RemoveFolderAsync(folder.ToUpperInvariant() + Path.DirectorySeparatorChar);
        Assert.Empty(new FavoritesStore(StorePath).FindFolders(folder));
        Assert.Equal(2, store.Entries.Count);
        Assert.Contains(store.Entries, entry => entry.Path == original);
        Assert.Equal("keep", File.ReadAllText(original));
    }

    [Fact]
    public async Task Groups_reordering_moves_and_names_survive_restart_without_touching_files()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "original.txt");
        await File.WriteAllTextAsync(file, "untouched");
        var store = new FavoritesStore(StorePath);
        await store.CreateGroupAsync("工作");
        var group = store.Entries.Single();
        await store.AddAsync([(file, false), (Path.Combine(_directory, "offline.pdf"), false)]);
        var first = store.Entries.Single(entry => entry.Path == file);
        await store.RenameAsync(first.Id, "常用文档");
        await store.MoveAsync(first.Id, group.Id);
        var reloaded = new FavoritesStore(StorePath);
        Assert.Null(reloaded.LoadError);
        Assert.Equal("常用文档", reloaded.Entries.Single(entry => entry.Id == first.Id).Name);
        Assert.Equal(group.Id, reloaded.Entries.Single(entry => entry.Id == first.Id).GroupId);
        await reloaded.RemoveAsync(group.Id);
        Assert.Single(reloaded.Entries);
        Assert.Equal("untouched", File.ReadAllText(file));
    }

    [Fact]
    public async Task Duplicate_paths_are_deduplicated_per_group_and_concurrent_adds_are_preserved()
    {
        var store = new FavoritesStore(StorePath);
        var path = Path.Combine(_directory, "item.txt");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.AddAsync([(path, false)])));
        Assert.Single(store.Entries);
        await store.CreateGroupAsync("分组");
        var group = store.Entries.Single(entry => entry.IsGroup);
        await store.AddAsync([(path, false)], group.Id);
        Assert.Equal(3, new FavoritesStore(StorePath).Entries.Count);
        var original = store.Entries.Single(entry => entry.Path == path && entry.GroupId is null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveAsync(original.Id, group.Id));
        Assert.Equal(3, store.Entries.Count);
    }

    [Fact]
    public async Task Reorder_and_drag_insertion_preserve_siblings_and_reject_group_cycles()
    {
        var store = new FavoritesStore(StorePath);
        await store.CreateGroupAsync("第一组");
        await store.CreateGroupAsync("第二组");
        var a = store.Entries[0]; var b = store.Entries[1];
        await store.ReorderAsync(b.Id, -1);
        Assert.Equal(b.Id, store.Entries[0].Id);
        await store.MoveAsync(a.Id, null, b.Id);
        Assert.Equal(a.Id, store.Entries[0].Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveAsync(a.Id, a.Id));
        await store.MoveAsync(a.Id, b.Id);
        Assert.Equal(b.Id, store.Entries.Single(entry => entry.Id == a.Id).GroupId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveAsync(b.Id, a.Id));
        Assert.Equal(2, new FavoritesStore(StorePath).Entries.Count);
    }

    [Fact]
    public async Task Nested_groups_persist_in_tree_order_and_delete_the_whole_subtree_only()
    {
        var store = new FavoritesStore(StorePath);
        await store.CreateGroupAsync("Work");
        var root = store.Entries.Single();
        await store.CreateGroupAsync("Projects", root.Id);
        var child = store.Entries.Last();
        await store.CreateGroupAsync("2026", child.Id);
        var leaf = store.Entries.Last();
        var path = Path.Combine(_directory, "original.txt");
        File.WriteAllText(path, "keep");
        await store.AddAsync([(path, false)], leaf.Id);
        await store.AddAsync([(path, false)]);
        await store.CreateGroupAsync("Personal");
        var reloaded = new FavoritesStore(StorePath);
        Assert.Null(reloaded.LoadError);
        Assert.Equal(["Work", "Work / Projects", "Work / Projects / 2026", "Personal"], reloaded.GroupsInTreeOrder().Select(group => group.Breadcrumb));
        Assert.Equal([0, 1, 2, 0], reloaded.GroupsInTreeOrder().Select(group => group.Depth));
        await reloaded.RemoveAsync(root.Id);
        Assert.Equal(2, new FavoritesStore(StorePath).Entries.Count);
        Assert.Single(reloaded.Entries, entry => entry.Path == path);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public async Task Moving_parent_and_child_together_keeps_the_subtree_and_rejects_cycles_atomically()
    {
        var store = new FavoritesStore(StorePath);
        await store.CreateGroupAsync("Root");
        var root = store.Entries.Single();
        await store.CreateGroupAsync("Child", root.Id);
        var child = store.Entries.Last();
        await store.CreateGroupAsync("Target");
        var target = store.Entries.Last();
        Assert.False(store.CanMoveTo([root.Id], child.Id));
        Assert.False(store.CanMoveTo([root.Id], root.Id));
        Assert.True(store.CanMoveTo([root.Id, child.Id], target.Id));
        await store.MoveManyAsync([root.Id, child.Id], target.Id);
        Assert.Equal(target.Id, store.Entries.Single(entry => entry.Id == root.Id).GroupId);
        Assert.Equal(root.Id, store.Entries.Single(entry => entry.Id == child.Id).GroupId);
        var before = File.ReadAllText(StorePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveManyAsync([target.Id, child.Id], root.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveAsync(target.Id, child.Id));
        Assert.Equal(before, File.ReadAllText(StorePath));
    }

    [Fact]
    public async Task Cyclic_import_is_preserved_and_deep_valid_groups_do_not_require_recursion()
    {
        Directory.CreateDirectory(_directory);
        var cycle = System.Text.Json.JsonSerializer.Serialize(new[] {
            new FavoriteEntry("a", "A", null, true, "b"), new FavoriteEntry("b", "B", null, true, "a") });
        File.WriteAllText(StorePath, cycle);
        var invalid = new FavoritesStore(StorePath);
        Assert.NotNull(invalid.LoadError);
        await Assert.ThrowsAsync<IOException>(() => invalid.CreateGroupAsync("test"));
        Assert.Equal(cycle, File.ReadAllText(StorePath));
        var deep = Enumerable.Range(0, 1500).Select(i => new FavoriteEntry(i.ToString(), "Group " + i, null, true, i == 0 ? null : (i - 1).ToString())).ToArray();
        File.WriteAllText(StorePath, System.Text.Json.JsonSerializer.Serialize(deep));
        var valid = new FavoritesStore(StorePath);
        Assert.Null(valid.LoadError);
        Assert.Equal(1500, valid.GroupsInTreeOrder().Count);
        Assert.All(valid.GroupsInTreeOrder(), group => Assert.True(group.Breadcrumb.Length <= 240));
        await valid.RemoveAsync("0");
        Assert.Empty(new FavoritesStore(StorePath).Entries);
    }

    [Fact]
    public async Task Corrupt_store_is_not_silently_overwritten()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(StorePath, "broken data");
        var store = new FavoritesStore(StorePath);
        Assert.NotNull(store.LoadError);
        await Assert.ThrowsAsync<IOException>(() => store.CreateGroupAsync("test"));
        Assert.Equal("broken data", File.ReadAllText(StorePath));
    }

    [Fact]
    public void Setup_is_pending_until_confirmed_and_hidden_bar_does_not_reset_completion()
    {
        Assert.False(FeatureSetup.Load(_directory).Completed);
        new FeatureSetup(true, true).Save(_directory);
        (FeatureSetup.Load(_directory) with { FavoritesBarEnabled = false }).Save(_directory);
        Assert.Equal(new FeatureSetup(true, false), FeatureSetup.Load(_directory));
    }

    [Fact]
    public async Task Batch_move_is_atomic_on_conflict_and_preserves_order_after_restart()
    {
        var store = new FavoritesStore(StorePath);
        await store.CreateGroupAsync("目标");
        var group = store.Entries.Single();
        var a = Path.Combine(_directory, "a.txt");
        var b = Path.Combine(_directory, "b.txt");
        await store.AddAsync([(a, false), (b, false)]);
        var ids = store.Entries.Where(entry => !entry.IsGroup).Select(entry => entry.Id).ToArray();
        await store.AddAsync([(b, false)], group.Id);
        var before = File.ReadAllText(StorePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveManyAsync(ids, group.Id));
        Assert.Equal(before, File.ReadAllText(StorePath));
        await store.RemoveManyAsync(store.Entries.Where(entry => entry.GroupId == group.Id).Select(entry => entry.Id));
        await store.MoveManyAsync(ids, group.Id);
        await store.SetOrderAsync(group.Id, ids.Reverse().ToArray());
        Assert.Equal(ids.Reverse(), new FavoritesStore(StorePath).Entries.Where(entry => entry.GroupId == group.Id).Select(entry => entry.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetOrderAsync(group.Id, [ids[0], ids[0]]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.MoveManyAsync([group.Id], group.Id));
    }

    [Fact]
    public async Task Batch_removal_cascades_references_without_removing_original_files()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "keep.txt");
        await File.WriteAllTextAsync(path, "keep");
        var store = new FavoritesStore(StorePath);
        await store.CreateGroupAsync("分组");
        var group = store.Entries.Single();
        await store.AddAsync([(path, false)], group.Id);
        await store.AddAsync([(path, false)]);
        await store.RemoveManyAsync([group.Id]);
        Assert.Single(new FavoritesStore(StorePath).Entries);
        Assert.Equal("keep", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
