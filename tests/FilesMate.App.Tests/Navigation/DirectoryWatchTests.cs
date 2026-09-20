using FilesMate.App.Navigation;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class DirectoryWatchTests
{
    [Fact]
    public async Task Disposed_pane_releases_published_folder_data_without_collecting_the_pane()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-dispose-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "keep.txt")];
            await using var vm = new PaneViewModel(new ImmediateUiDispatcher(), new WindowsPathService(),
                enumerator, NaturalStringComparer.Instance);
            vm.Navigate(root);
            await vm.WhenCurrentSessionCompletes;
            Assert.NotNull(vm.ViewIndex);
            Assert.NotEmpty(vm.PlaceholderNames);

            await vm.DisposeAsync();

            Assert.Null(vm.Store);
            Assert.Null(vm.ViewIndex);
            Assert.Empty(vm.PlaceholderNames);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Burst_coalesces_ui_dispatches_instead_of_posting_for_each_notification()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-burst-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "keep.txt")];
            var watcher = new FakeDirectoryWatcher();
            var dispatcher = new CountingDispatcher();
            await using var vm = new PaneViewModel(dispatcher, new WindowsPathService(),
                enumerator, NaturalStringComparer.Instance, watcher);
            vm.Navigate(root);
            await WaitUntil(() => !vm.IsLoading && watcher.Started == 1);
            var baseline = dispatcher.Posts;
            for (var i = 0; i < 1000; i++)
                watcher.Notifications.Writer.TryWrite(Notice(vm, DirectoryWatchKind.Deleted, "missing-" + i));
            await WaitUntil(() => watcher.Notifications.Reader.Count == 0);
            await Task.Delay(PaneViewModel.WatchDebounce + TimeSpan.FromMilliseconds(300));

            Assert.InRange(dispatcher.Posts - baseline, 1, 80);
            Assert.Equal(["keep.txt"], vm.PlaceholderNames);
            Assert.Equal(1, enumerator.Started);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CountingDispatcher : IUiDispatcher
    {
        private int _posts;
        public int Posts => Volatile.Read(ref _posts);
        public bool HasThreadAccess => true;
        public void Post(Action action)
        {
            Interlocked.Increment(ref _posts);
            action();
        }
    }

    [Fact]
    public async Task Home_does_not_start_a_directory_watch()
    {
        var watcher = new FakeDirectoryWatcher();
        await using var vm = new PaneViewModel(
            new ImmediateUiDispatcher(),
            new WindowsPathService(),
            new FakeDirectoryEnumerator(),
            NaturalStringComparer.Instance,
            watcher);
        vm.Navigate(HomeLocation.Uri);
        await vm.WhenCurrentSessionCompletes;
        Assert.Equal(0, watcher.Started);
    }

    [Fact]
    public async Task Watch_created_patches_without_reenumerating()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "old.txt")];
            var watcher = new FakeDirectoryWatcher();
            await using var vm = Open(root, enumerator, watcher);
            await vm.WhenCurrentSessionCompletes;
            await WaitUntil(() => watcher.Started == 1);

            Assert.Equal(["old.txt"], vm.PlaceholderNames);
            File.WriteAllText(Path.Combine(root, "pkg.exe"), "ok");
            Assert.True(watcher.Notifications.Writer.TryWrite(
                Notice(vm, DirectoryWatchKind.Created, "pkg.exe")));
            await WaitUntil(() => vm.PlaceholderNames.Contains("pkg.exe"));

            Assert.Contains("pkg.exe", vm.PlaceholderNames);
            Assert.Contains("old.txt", vm.PlaceholderNames);
            Assert.Equal(1, enumerator.Started);
            Assert.Equal(1, watcher.Started);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Watch_deleted_removes_the_row_without_reenumerating()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-del-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] =
            [
                FakeDirectoryEnumerator.Entry(1, "old.txt"),
                FakeDirectoryEnumerator.Entry(2, "gone.txt"),
            ];
            var watcher = new FakeDirectoryWatcher();
            await using var vm = Open(root, enumerator, watcher);
            await vm.WhenCurrentSessionCompletes;
            await WaitUntil(() => watcher.Started == 1);

            Assert.True(watcher.Notifications.Writer.TryWrite(
                Notice(vm, DirectoryWatchKind.Deleted, "gone.txt")));
            await WaitUntil(() => !vm.PlaceholderNames.Contains("gone.txt"));

            Assert.Equal(["old.txt"], vm.PlaceholderNames);
            Assert.Equal(1, enumerator.Started);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Watch_renamed_keeps_the_row_id_without_reenumerating()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-ren-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "old.txt")];
            var watcher = new FakeDirectoryWatcher();
            await using var vm = Open(root, enumerator, watcher);
            await vm.WhenCurrentSessionCompletes;
            await WaitUntil(() => watcher.Started == 1);
            var id = vm.Store![0].Id;

            File.WriteAllText(Path.Combine(root, "new.txt"), "ok");
            Assert.True(watcher.Notifications.Writer.TryWrite(
                Notice(vm, DirectoryWatchKind.Renamed, "new.txt", "old.txt")));
            await WaitUntil(() => vm.PlaceholderNames.Contains("new.txt"));

            Assert.Equal(["new.txt"], vm.PlaceholderNames);
            Assert.Equal(id, vm.Store[0].Id);
            Assert.Equal(1, enumerator.Started);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Watch_overflow_reenumerates_without_blanking_first()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-live-ovf-").FullName;
        try
        {
            var enumerator = new FakeDirectoryEnumerator();
            enumerator.Folders[root] = [FakeDirectoryEnumerator.Entry(1, "old.txt")];
            var watcher = new FakeDirectoryWatcher();
            await using var vm = Open(root, enumerator, watcher);
            await vm.WhenCurrentSessionCompletes;
            await WaitUntil(() => watcher.Started == 1);

            enumerator.Folders[root] =
            [
                FakeDirectoryEnumerator.Entry(1, "old.txt"),
                FakeDirectoryEnumerator.Entry(2, "pkg.exe"),
            ];
            Assert.True(watcher.Notifications.Writer.TryWrite(
                Notice(vm, DirectoryWatchKind.Overflow, string.Empty)));
            await WaitUntil(() => enumerator.Started == 2 && vm.PlaceholderNames.Contains("pkg.exe"));

            Assert.Contains("pkg.exe", vm.PlaceholderNames);
            Assert.Contains("old.txt", vm.PlaceholderNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PaneViewModel Open(string root, FakeDirectoryEnumerator enumerator, FakeDirectoryWatcher watcher)
    {
        var vm = new PaneViewModel(
            new ImmediateUiDispatcher(),
            new WindowsPathService(),
            enumerator,
            NaturalStringComparer.Instance,
            watcher);
        vm.Navigate(root);
        return vm;
    }

    private static DirectoryWatchNotification Notice(
        PaneViewModel vm,
        DirectoryWatchKind kind,
        string name,
        string oldName = "") =>
        new(vm.Navigation.PaneId, vm.Navigation.CurrentGeneration, kind, name, oldName);

    private static async Task WaitUntil(Func<bool> ready)
    {
        var started = DateTime.UtcNow;
        while (!ready() && DateTime.UtcNow - started < TimeSpan.FromSeconds(3))
        {
            await Task.Delay(20);
        }

        Assert.True(ready());
    }
}
