using FilesMate.App.Commands;
using FilesMate.App.Navigation;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Tests.Navigation;

public sealed class PortableDeviceNavigationTests
{
    private static PortableDeviceLocation Root => new(PortableDeviceLocation.ComputerPrefix
        + @"\\?\usb#fixture#" + PortableDeviceLocation.InterfaceId, "Phone", []);

    [Fact]
    public void Opening_child_in_new_tab_inherits_origin_without_changing_source_history()
    {
        var source = new NavigationController(new WindowsPathService(), PaneId.New());
        source.Open(HomeLocation.Uri, true);
        source.Open(@"D:\folder", true);
        source.Open(@"D:\other", true);
        source.Back();
        var tab = new NavigationController(new WindowsPathService(), PaneId.New());
        tab.Open(@"D:\folder\child", true);
        tab.RestoreHistory(source.HistoryForNewTab(tab.CurrentPath!));
        Assert.Equal(@"D:\folder", tab.Back()?.Path);
        Assert.Equal(@"D:\folder\child", tab.Forward()?.Path);
        Assert.Equal(@"D:\other", source.Forward()?.Path);
        tab.Back();
        Assert.Equal(HomeLocation.Uri, tab.Back()?.Path);
    }

    [Fact]
    public void Device_history_and_parent_use_opaque_identities()
    {
        var storage = Root.Child("Storage", Root.Root + @"\opaque-storage");
        var child = storage.Child("Photos", storage.ParsingName + @"\opaque-photos");
        var nav = new NavigationController(new WindowsPathService(), PaneId.New());
        nav.Open(HomeLocation.Uri, true); nav.Open(Root.Uri, true); nav.Open(storage.Uri, true); nav.Open(child.Uri, true);
        Assert.Equal(storage.Uri, nav.Back()?.Path);
        Assert.Equal(child.Uri, nav.Forward()?.Path);
        Assert.Equal(storage.Uri, nav.Up()?.Path);
        Assert.Equal(Root.Uri, nav.Up()?.Path);
        Assert.Equal(HomeLocation.Uri, nav.Up()?.Path);
        Assert.Null(nav.Up());
    }

    [Fact]
    public async Task Device_rows_keep_names_for_sorting_and_unique_identities_for_operations()
    {
        var a = Root.Child("same.jpg", Root.Root + @"\one");
        var b = Root.Child("same.jpg", Root.Root + @"\two");
        var provider = Provider((_, _) => Task.FromResult<IReadOnlyList<PortableDeviceEntry>>(
            [new("same.jpg", a, false, 7, null), new("same.jpg", b, false, 9, null)]));
        var batch = Assert.Single(await provider.EnumerateAsync(Request(Root.Uri, 1), default).ToListAsync());
        Assert.Equal(2, batch.Entries.Count);
        Assert.All(batch.Entries, e => Assert.Equal("same.jpg", e.Name));
        Assert.Equal(a.Uri, provider.Resolve(Root.Uri, 1, batch.Entries[0].Id));
        Assert.Equal(b.Uri, provider.Resolve(Root.Uri, 1, batch.Entries[1].Id));
        Assert.True(provider.CanReceiveFiles(Root.Uri, 1));
        Assert.Null(provider.Resolve(Root.Uri, 2, 1));
        provider.Clear();
        Assert.Null(provider.Resolve(Root.Uri, 1, 1));
        Assert.False(provider.CanReceiveFiles(Root.Uri, 1));
    }

    [Fact]
    public async Task Late_driver_result_cannot_replace_the_new_folders_identities()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<PortableDeviceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = Root.Child("Next", Root.Root + @"\next");
        var child = next.Child("new.txt", next.ParsingName + @"\new");
        var provider = Provider((path, _) =>
        {
            if (path.Uri == Root.Uri) { started.SetResult(); return pending.Task; }
            return Task.FromResult<IReadOnlyList<PortableDeviceEntry>>([new("new.txt", child, false, 1, null)]);
        });
        var old = provider.EnumerateAsync(Request(Root.Uri, 1), default).ToListAsync().AsTask();
        await started.Task;
        await provider.EnumerateAsync(Request(next.Uri, 2), default).ToListAsync();
        pending.SetResult([]);
        Assert.Empty(await old);
        Assert.Equal(child.Uri, provider.Resolve(next.Uri, 2, 1));
        Assert.Empty(await provider.EnumerateAsync(Request(Root.Uri, 1), default).ToListAsync());
        Assert.Equal(child.Uri, provider.Resolve(next.Uri, 2, 1));
        await provider.EnumerateAsync(Request(@"D:\local", 3), default).ToListAsync();
        Assert.Null(provider.Resolve(next.Uri, 2, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Device_commands_follow_write_capability_without_exposing_local_mutations(bool writable)
    {
        var context = CommandContext.ForToolbar(1, isFolderWritable: writable, clipboardHasFiles: true) with
        { IsPortableDevice = true, FolderPath = Root.Uri };
        Assert.Equal(writable, CommandCatalog.CanExecute(AppCommandId.Paste, context));
        Assert.True(CommandCatalog.CanExecute(AppCommandId.Copy, context));
        foreach (var id in new[] { AppCommandId.Cut, AppCommandId.Rename, AppCommandId.Recycle,
            AppCommandId.PermanentDelete, AppCommandId.NewFolder, AppCommandId.CompressZip, AppCommandId.OpenInTerminal })
            Assert.False(CommandCatalog.CanExecute(id, context));
    }

    [Fact]
    public async Task Closing_pane_during_a_driver_read_does_not_restore_its_file_map()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<PortableDeviceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var file = Root.Child("photo.jpg", Root.Root + @"\photo");
        var provider = Provider((_, _) => { started.SetResult(); return pending.Task; });
        var reading = provider.EnumerateAsync(Request(Root.Uri, 1), default).ToListAsync().AsTask();
        await started.Task;
        provider.Clear();
        pending.SetResult([new("photo.jpg", file, false, 10, null)]);
        Assert.Empty(await reading);
        Assert.Null(provider.Resolve(Root.Uri, 1, 1));
        Assert.False(provider.CanReceiveFiles(Root.Uri, 1));
    }

    private static DirectoryRequest Request(string path, long generation) => new(PaneId.New(), generation, path, DirectoryReadOptions.Default);
    private static PortableDeviceDirectoryEnumerator Provider(Func<PortableDeviceLocation, CancellationToken, Task<IReadOnlyList<PortableDeviceEntry>>> read) =>
        new(new EmptyEnumerator(), read, (_, _) => Task.FromResult(new DeviceFolderCapabilities(true)));

    private sealed class EmptyEnumerator : IDirectoryEnumerator
    {
        public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(DirectoryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return DirectoryBatch.Create(request.PaneId, request.Generation, request.Path, [], true, null);
        }
    }
}
