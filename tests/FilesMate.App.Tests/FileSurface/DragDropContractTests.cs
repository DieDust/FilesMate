using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.FileSurface;

public sealed class DragDropContractTests
{
    [Fact]
    public void File_surface_uses_storage_item_drag_and_drop_without_disabling_virtualization()
    {
        var xaml = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml"));
        var code = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.xaml.cs"));

        Assert.Contains("AllowDrop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragStarting=\"OnDragStarting\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"OnDrop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetStorageItems", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetContentFromDataPackage", code, StringComparison.Ordinal);
        Assert.Contains("SetContentFromSoftwareBitmap", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DragUIOverride.IsContentVisible = false", code, StringComparison.Ordinal);
        Assert.Contains("StartDragAsync", code, StringComparison.Ordinal);
        Assert.Contains("private void OnDragStarting", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private async void OnDragStarting", code, StringComparison.Ordinal);
        Assert.Contains("DropRequested", code, StringComparison.Ordinal);
        Assert.Contains("ItemsRepeater", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalCacheLength=\"1\"", xaml, StringComparison.Ordinal);
        var begin = code.IndexOf("private async Task BeginExternalDragAsync", StringComparison.Ordinal);
        var startDrag = code.IndexOf("StartDragAsync(", begin, StringComparison.Ordinal);
        var release = code.IndexOf("ReleasePointerCaptures()", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && release >= 0 && startDrag > release);
    }

    [Fact]
    public void Navigator_routes_drops_through_the_existing_file_operation_boundary()
    {
        var navigator = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Views",
            "NavigatorPage.xaml.cs"));
        var actions = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Views",
            "PaneFileActions.cs"));

        Assert.Contains("FileSurface.DropRequested", navigator, StringComparison.Ordinal);
        Assert.Contains("HandleFileDropAsync", navigator, StringComparison.Ordinal);
        Assert.Contains("await FileShelfTransfer.RunAsync(_operations", actions, StringComparison.Ordinal);
        var transfer = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Services", "FileShelfTransfer.cs"));
        Assert.Contains("FileDropPolicy.FilterSources", transfer, StringComparison.Ordinal);
        Assert.DoesNotContain("FileDropPolicy.FilterSources", actions, StringComparison.Ordinal);
        Assert.Contains("WindowsFileTransfer.RunAsync", transfer, StringComparison.Ordinal);
        Assert.Contains("FileConflictDialog.For(_host)", actions, StringComparison.Ordinal);
    }
}
