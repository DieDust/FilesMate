using System.Collections.Concurrent;
using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Metadata;

namespace FilesMate.App.Tests.Navigation;

public sealed class PinnedFolderRenameTests
{
    [Fact]
    public void Relocating_parent_keeps_descendant_pins_sidebar_order_and_other_settings()
    {
        using var fixture = new Fixture();
        var store = fixture.Store;
        store.Save([@"C:\Work", @"C:\Work\Child", @"C:\Work-other", @"D:\Other"]);
        store.SaveItemOrder([@"custom:C:\Work\Child", "downloads", @"custom:C:\Work", @"custom:C:\Work-other"]);
        store.AddCloud(@"D:\Cloud"); store.HideDefault("pictures"); store.SetSectionExpanded("pinned", false);
        Assert.True(store.Relocate(@"c:\work\", @"C:\Renamed"));
        var state = new PinnedLocationStore(store.FilePath).LoadState();
        Assert.Equal([@"C:\Renamed", @"C:\Renamed\Child", @"C:\Work-other", @"D:\Other"], state.Paths);
        Assert.Equal([@"custom:c:\renamed\child", "downloads", @"custom:c:\renamed", @"custom:c:\work-other"], state.ItemOrder);
        Assert.Equal([@"D:\Cloud"], state.CloudPaths); Assert.Equal(["pictures"], state.HiddenDefaultIds);
        Assert.Equal(["pinned"], state.CollapsedSections);
        Assert.False(store.Relocate(@"C:\Missing", @"C:\Elsewhere"));
    }

    [Fact]
    public void Case_only_rename_and_duplicate_target_are_persisted()
    {
        using var fixture = new Fixture();
        fixture.Store.Save([@"C:\name", @"C:\Other"]);
        Assert.True(fixture.Store.Relocate(@"C:\name", @"C:\NAME"));
        Assert.Equal(@"C:\NAME", fixture.Store.Load()[0]);
        Assert.True(fixture.Store.Relocate(@"C:\NAME", @"C:\Other"));
        Assert.Equal([@"C:\Other"], fixture.Store.Load());
    }

    [Fact]
    public async Task External_rename_updates_and_persists_target_and_ancestor_rename_survives_restart()
    {
        using var fixture = new Fixture();
        var original = Directory.CreateDirectory(Path.Combine(fixture.Root, "parent", "folder")).FullName;
        fixture.Store.Add(original);
        Assert.NotNull(Assert.Single(fixture.Store.LoadState().FolderReferences!).Identity);
        var queue = new ConcurrentQueue<Action>();
        var changed = 0;
        using (var tracker = new PinnedLocationTracker(fixture.Store, queue.Enqueue, () => changed++))
        {
            var renamed = Path.Combine(Path.GetDirectoryName(original)!, "new name");
            Directory.Move(original, renamed);
            await WaitFor(() => fixture.Store.IsPinned(renamed), queue);
            Assert.True(changed > 0);
            Assert.Equal(renamed, Assert.Single(new PinnedLocationStore(fixture.Store.FilePath).Load()));
        }
        var newParent = Path.Combine(fixture.Root, "renamed parent");
        Directory.Move(Path.Combine(fixture.Root, "parent"), newParent);
        var final = Path.Combine(newParent, "new name");
        using var restarted = new PinnedLocationTracker(new(fixture.Store.FilePath), queue.Enqueue, () => changed++);
        await WaitFor(() => fixture.Store.IsPinned(final), queue);
        Assert.Equal(final, WindowsDirectoryReferences.Resolve(Assert.Single(fixture.Store.LoadState().FolderReferences!).Identity));
    }

    [Fact]
    public async Task Old_path_reuse_does_not_redirect_pin_to_a_different_folder()
    {
        using var fixture = new Fixture();
        var original = Directory.CreateDirectory(Path.Combine(fixture.Root, "folder")).FullName;
        fixture.Store.Add(original);
        var saved = Assert.Single(fixture.Store.LoadState().FolderReferences!).Identity;
        var renamed = Path.Combine(fixture.Root, "same folder renamed");
        Directory.Move(original, renamed);
        Directory.CreateDirectory(original);
        Assert.NotEqual(saved, WindowsDirectoryReferences.Capture(original));
        var queue = new ConcurrentQueue<Action>();
        using var tracker = new PinnedLocationTracker(fixture.Store, queue.Enqueue, () => { });
        await WaitFor(() => fixture.Store.IsPinned(renamed), queue);
        Assert.False(fixture.Store.IsPinned(original));
        Directory.Delete(renamed);
        Assert.Null(WindowsDirectoryReferences.Resolve(saved));
        var unrelated = Path.Combine(fixture.Root, "unrelated renamed");
        Directory.Move(original, unrelated);
        tracker.CheckNow();
        await Task.Delay(180);
        while (queue.TryDequeue(out var action)) action();
        Assert.False(fixture.Store.IsPinned(unrelated));
        Assert.Equal(renamed, Assert.Single(fixture.Store.Load()));
    }

    [Fact]
    public void Legacy_paths_gain_identity_without_losing_hidden_defaults_or_order()
    {
        using var fixture = new Fixture();
        var path = Directory.CreateDirectory(Path.Combine(fixture.Root, "legacy")).FullName;
        File.WriteAllText(fixture.Store.FilePath, System.Text.Json.JsonSerializer.Serialize(new[] { path }));
        fixture.Store.RememberFolderReferences();
        fixture.Store.HideDefault("documents"); fixture.Store.SaveItemOrder(["downloads", "custom:" + path]);
        var reference = Assert.Single(new PinnedLocationStore(fixture.Store.FilePath).LoadState().FolderReferences!);
        Assert.Equal(path, reference.Path);
        Assert.Equal(path, WindowsDirectoryReferences.Resolve(reference.Identity));
    }

    private static async Task WaitFor(Func<bool> done, ConcurrentQueue<Action> queue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            while (queue.TryDequeue(out var action)) action();
            if (done()) return;
            await Task.Delay(30);
        } while (DateTime.UtcNow < deadline);
        Assert.True(done(), "Pinned folder did not follow the rename.");
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FilesMate", "pin-rename-" + Guid.NewGuid().ToString("N"));
        public PinnedLocationStore Store { get; }
        public Fixture() { Directory.CreateDirectory(Root); Store = new(Path.Combine(Root, "pins.json")); }
        public void Dispose() { Directory.Delete(Root, true); }
    }
}
