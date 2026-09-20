using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Icons;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using FilesMate.Core.Icons;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Tests.Settings;

public sealed class FolderCustomizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-features-" + Guid.NewGuid().ToString("N"));
    private string Config => Path.Combine(_root, "settings.json");

    public FolderCustomizationTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Global_view_changes_all_folders_without_destroying_local_views_or_covers()
    {
        var store = new FolderCustomizationStore(Config);
        var a = Path.Combine(_root, "a"); var b = Path.Combine(_root, "b");
        var aView = new FolderViewSettings(true, GridSizePreset.Default.Slot, EntrySort.Name);
        var bView = new FolderViewSettings(false, GridSizePreset.All.Last().Slot, EntrySort.Modified);
        await store.SetViewAsync(a, aView); await store.SetViewAsync(b, bView);
        await store.SetCoverAsync(a, Path.Combine(a, "cover.jpg"));
        Assert.False(await store.GetGlobalViewAsync());
        await store.SetGlobalViewAsync(true);
        var global = aView with { Sort = EntrySort.Modified with { Ascending = false } };
        await store.SetViewAsync(a, global);
        Assert.Equal(global, (await store.GetAsync(b)).View);
        var restarted = new FolderCustomizationStore(Config);
        Assert.True(await restarted.GetGlobalViewAsync());
        Assert.Equal(global, (await restarted.GetAsync(b)).View);
        await restarted.SetGlobalViewAsync(false);
        Assert.Equal(aView, (await restarted.GetAsync(a)).View);
        Assert.Equal(bView, (await restarted.GetAsync(b)).View);
        Assert.Equal(Path.Combine(a, "cover.jpg"), (await restarted.GetAsync(a)).CoverPath);
        await restarted.SetGlobalViewAsync(true);
        Assert.Equal(global, (await restarted.GetAsync(a)).View);
    }

    [Fact]
    public async Task Reset_global_defaults_survives_switching_scope()
    {
        var store = new FolderCustomizationStore(Config);
        var path = Path.Combine(_root, "a");
        await store.SetViewAsync(path, new(true, GridSizePreset.Default.Slot, EntrySort.Modified));
        await store.SetGlobalViewAsync(true);
        await store.SetViewAsync(path, null);
        await store.SetGlobalViewAsync(false);
        Assert.NotNull((await store.GetAsync(path)).View);
        await store.SetGlobalViewAsync(true);
        Assert.Null((await store.GetAsync(path)).View);
    }

    [Fact]
    public async Task Every_zoom_size_keeps_date_sort_and_direction_after_restart()
    {
        var store = new FolderCustomizationStore(Config);
        foreach (var preset in GridSizePreset.All)
        {
            var folder = Path.Combine(_root, preset.Slot.ToString());
            await store.SetViewAsync(folder, new(false, preset.Slot, EntrySort.Modified with { Ascending = false }));
        }

        var restarted = new FolderCustomizationStore(Config);
        foreach (var preset in GridSizePreset.All)
        {
            var view = (await restarted.GetAsync(Path.Combine(_root, preset.Slot.ToString()))).View!;
            Assert.Equal(preset.Slot, view.GridSlot);
            Assert.Equal(EntrySortColumn.Modified, view.Sort.Column);
            Assert.False(view.Sort.Ascending);
        }
    }

    [Fact]
    public async Task Closing_during_debounce_flushes_the_last_sort_choice()
    {
        var store = new FolderCustomizationStore(Config);
        await store.GetAsync(_root);
        var view = new FolderViewSettings(false, GridSizePreset.Maximum.Slot, EntrySort.Modified with { Ascending = false });
        var pendingSave = store.SetViewAsync(_root, view);
        await store.FlushAsync();
        Assert.Equal(view, (await new FolderCustomizationStore(Config).GetAsync(_root)).View);
        await pendingSave;
    }

    [Fact]
    public async Task Unknown_zoom_slot_does_not_discard_valid_sort_settings()
    {
        await File.WriteAllTextAsync(Config, System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, FolderCustomization> { [_root] = new(new(false, 999, EntrySort.Modified)) }));
        var view = (await new FolderCustomizationStore(Config).GetAsync(_root)).View!;
        Assert.Equal(EntrySort.Modified, view.Sort);
        Assert.Equal(GridSizePreset.Default.Slot, view.GridSlot);
    }

    [Fact]
    public async Task View_and_cover_are_independent_and_survive_restart()
    {
        var store = new FolderCustomizationStore(Config);
        var a = Path.Combine(_root, "a");
        var b = Path.Combine(_root, "b");
        var view = new FolderViewSettings(false, 200, EntrySort.Size with { Ascending = false });
        await store.SetViewAsync(a, view);
        await store.SetCoverAsync(a, Path.Combine(a, "video.mp4"));
        await store.SetViewAsync(b, new(true, 96, EntrySort.Name));
        var reloaded = new FolderCustomizationStore(Config);
        Assert.Equal(view, (await reloaded.GetAsync(a.ToUpperInvariant())).View);
        Assert.True((await reloaded.GetAsync(b)).View!.Details);
        Assert.Equal(Path.Combine(a, "video.mp4"), (await reloaded.GetAsync(a)).CoverPath);
        Assert.Null((await reloaded.GetAsync(Path.Combine(_root, "unconfigured"))).View);
        await reloaded.SetViewAsync(a, null);
        Assert.NotNull((await reloaded.GetAsync(a)).CoverPath);
        await reloaded.SetCoverAsync(a, null);
        Assert.Equal(new FolderCustomization(), await new FolderCustomizationStore(Config).GetAsync(a));
    }

    [Fact]
    public async Task Concurrent_changes_are_not_lost()
    {
        var store = new FolderCustomizationStore(Config);
        var folders = Enumerable.Range(0, 20).Select(i => Path.Combine(_root, i.ToString())).ToArray();
        await Task.WhenAll(folders.Select(folder => store.SetViewAsync(folder, new(true, 120, EntrySort.Name))));
        var reloaded = new FolderCustomizationStore(Config);
        foreach (var folder in folders) Assert.NotNull((await reloaded.GetAsync(folder)).View);
    }

    [Fact]
    public async Task Corrupt_configuration_and_virtual_locations_fall_back()
    {
        await File.WriteAllTextAsync(Config, "{broken");
        var store = new FolderCustomizationStore(Config);
        Assert.Equal(new FolderCustomization(), await store.GetAsync(_root));
        Assert.Null(FolderCustomizationStore.Key("home://"));
        Assert.Null(FolderCustomizationStore.Key("relative"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetViewAsync("home://", new(true, 120, EntrySort.Name)));
        await store.SetViewAsync(_root, new(true, 120, EntrySort.Name));
        Assert.NotNull((await new FolderCustomizationStore(Config).GetAsync(_root)).View);
    }

    [Fact]
    public async Task Cover_must_be_a_direct_child()
    {
        var store = new FolderCustomizationStore(Config);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetCoverAsync(_root, Path.Combine(_root, "nested", "image.png")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetCoverAsync(_root, "image.png"));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Custom_cover_is_preferred_with_automatic_fallback(bool exists, bool decodable)
    {
        var coverPath = Path.Combine(_root, "cover.mp4");
        if (exists) await File.WriteAllTextAsync(coverPath, "fixture");
        var bitmap = new IconBitmap(1, 1, new byte[4]);
        var scanned = 0;
        var requested = new List<string>();
        var service = new FolderPreviewService((path, _, _) =>
        {
            requested.Add(path);
            return Task.FromResult<IconBitmap?>(path == coverPath && !decodable ? null : bitmap);
        }, (_, _) => { scanned++; return ["automatic.jpg"]; }, cover: _ => Task.FromResult<string?>(coverPath));
        Assert.Same(bitmap, await service.GetAsync(_root, 48, CancellationToken.None));
        Assert.Equal(exists && decodable ? 0 : 1, scanned);
        Assert.Equal(exists ? coverPath : "automatic.jpg", requested[0]);
    }

    [Fact]
    public async Task Shelf_stores_references_only_and_deduplicates_across_restarts()
    {
        var source = Path.Combine(_root, "original.txt");
        await File.WriteAllTextAsync(source, "unchanged");
        var store = new FileShelfStore(Config);
        await store.AddAsync([source, source.ToUpperInvariant(), "home://"]);
        Assert.Single(await store.GetAsync());
        Assert.Equal("unchanged", await File.ReadAllTextAsync(source));
        Assert.Equal(2, Directory.GetFiles(_root).Length);
        var reloaded = new FileShelfStore(Config);
        Assert.Equal(source, Assert.Single(await reloaded.GetAsync()));
        await reloaded.RemoveAsync([source.ToUpperInvariant()]);
        Assert.Empty(await reloaded.GetAsync());
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Shelf_capacity_failure_keeps_previous_state()
    {
        var store = new FileShelfStore(Config);
        await store.AddAsync([Path.Combine(_root, "first")]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(
            Enumerable.Range(0, FileShelfStore.Capacity).Select(i => Path.Combine(_root, i.ToString()))));
        Assert.Single(await store.GetAsync());
    }

    [Fact]
    public async Task Copy_continues_after_missing_item_and_does_not_overwrite()
    {
        var source = Path.Combine(_root, "one.txt");
        await File.WriteAllTextAsync(source, "new");
        var destination = Path.Combine(_root, "out");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "one.txt"), "old");
        var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(),
            [Path.Combine(_root, "missing"), source], destination, false,
            resolveConflict: (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.KeepBoth)));
        Assert.Single(result.Errors);
        var pair = Assert.Single(result.Completed);
        Assert.Equal("new", await File.ReadAllTextAsync(pair.Destination));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(destination, "one.txt")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Moving_parent_and_child_once_records_undo_pair()
    {
        var source = Path.Combine(_root, "folder");
        Directory.CreateDirectory(source);
        var child = Path.Combine(source, "one.txt");
        await File.WriteAllTextAsync(child, "content");
        var destination = Path.Combine(_root, "out");
        var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(),
            [child, source], destination, true);
        Assert.Empty(result.Errors);
        var pair = Assert.Single(result.Completed);
        Assert.False(Directory.Exists(source));
        Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(pair.Destination, "one.txt")));
        new WindowsLocalFileOperations().Rename(pair.Destination, pair.Source);
        Assert.True(File.Exists(child));
    }

    [Fact]
    public async Task Cancellation_retains_successful_work()
    {
        using var cts = new CancellationTokenSource();
        var a = Path.Combine(_root, "a.txt");
        var b = Path.Combine(_root, "b.txt");
        await File.WriteAllTextAsync(a, "a");
        await File.WriteAllTextAsync(b, "b");
        var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(),
            [a, b], Path.Combine(_root, "out"), true, new ImmediateProgress(_ => cts.Cancel()), cts.Token);
        Assert.True(result.Cancelled);
        Assert.Single(result.Completed);
        Assert.False(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task Destination_inside_source_is_rejected()
    {
        var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(),
            [_root], Path.Combine(_root, "nested"), false);
        Assert.Empty(result.Completed);
        Assert.Single(result.Errors);
        Assert.False(Directory.Exists(Path.Combine(_root, "nested")));
    }

    [Fact]
    public async Task Cutting_into_the_same_folder_keeps_the_file_without_an_error_or_clipboard_consumption()
    {
        var source = Path.Combine(_root, "same-parent.txt");
        File.WriteAllText(source, "keep");
        var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(), [source], _root, true);
        Assert.Empty(result.Errors); Assert.Empty(result.Completed); Assert.Equal(1, result.Skipped);
        Assert.Null(result.Undo); Assert.Equal("keep", File.ReadAllText(source));
    }

    [Fact]
    public void Cover_commands_require_one_folder_and_shelf_requires_selection()
    {
        Assert.True(CommandCatalog.Resolve(AppCommandId.ChooseFolderCover, CommandContext.ForMenu(1, primaryIsDirectory: true)).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.ChooseFolderCover, CommandContext.ForMenu(1)).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.ChooseFolderCover, CommandContext.ForMenu(2, primaryIsDirectory: true)).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.AddToShelf, CommandContext.Blank).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.ShowShelf, CommandContext.Blank).Enabled);
    }

    private sealed class ImmediateProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    [Fact]
    public async Task Shelf_change_notifications_see_committed_state_without_deadlock()
    {
        var shelf = new FileShelfStore(Config);
        var observed = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        shelf.Changed += async (_, _) => observed.TrySetResult(await shelf.GetAsync());
        await shelf.AddAsync([Path.Combine(_root, "reference.txt")]);
        Assert.Single(await observed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
