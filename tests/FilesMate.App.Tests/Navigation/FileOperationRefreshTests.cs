using FilesMate.App.Navigation;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class FileOperationRefreshTests
{
    [Fact]
    public async Task Operation_refresh_preserves_generation_store_and_surviving_ids()
    {
        var path = @"C:\operation-refresh";
        var enumerator = new FakeDirectoryEnumerator();
        enumerator.Folders[path] = [FakeDirectoryEnumerator.Entry(1, "keep"), FakeDirectoryEnumerator.Entry(2, "deleted")];
        await using var pane = new PaneViewModel(new ImmediateUiDispatcher(), new WindowsPathService(), enumerator, NaturalStringComparer.Instance);
        pane.Navigate(path);
        await pane.WhenCurrentSessionCompletes;
        await Wait(() => !pane.IsLoading && pane.ViewIndex is not null);
        var generation = pane.Navigation.CurrentGeneration;
        var store = pane.Store;
        enumerator.Folders[path] = [FakeDirectoryEnumerator.Entry(90, "created"), FakeDirectoryEnumerator.Entry(91, "keep")];
        pane.RefreshAfterFileOperation();
        await Wait(() => pane.PlaceholderNames.Contains("created"));
        Assert.Same(store, pane.Store);
        Assert.Equal(generation, pane.Navigation.CurrentGeneration);
        Assert.DoesNotContain("deleted", pane.PlaceholderNames);
        Assert.Equal(1, pane.Store!.Snapshot().Single(e => e.Name == "keep").Id);
        Assert.True(pane.Store.Snapshot().Single(e => e.Name == "created").Id > 2);
    }

    [Fact]
    public async Task Failed_reconciliation_keeps_current_items_visible()
    {
        var path = @"C:\operation-failure";
        var enumerator = new FakeDirectoryEnumerator();
        enumerator.Folders[path] = [FakeDirectoryEnumerator.Entry(1, "keep")];
        await using var pane = new PaneViewModel(new ImmediateUiDispatcher(), new WindowsPathService(), enumerator, NaturalStringComparer.Instance);
        pane.Navigate(path);
        await pane.WhenCurrentSessionCompletes;
        await Wait(() => !pane.IsLoading && pane.ViewIndex is not null);
        var index = pane.ViewIndex;
        enumerator.Errors[path] = new(FilesMate.Core.Directories.DirectoryReadErrorKind.AccessDenied, 5, "Refresh denied", true);
        pane.RefreshAfterFileOperation();
        await Wait(() => pane.StatusText == "Refresh denied");
        Assert.Same(index, pane.ViewIndex);
        Assert.Equal(["keep"], pane.PlaceholderNames);
    }

    private static async Task Wait(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(ready());
    }
}
