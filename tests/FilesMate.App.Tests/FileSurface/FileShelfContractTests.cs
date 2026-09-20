using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.FileSurface;

public sealed class FileShelfContractTests
{
    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([ThemeXaml.AppRoot, .. path]));

    [Fact]
    public void Shelf_is_an_anchored_nonmodal_card_with_no_destination_form()
    {
        var page = Read("Views", "NavigatorPage.xaml");
        var panel = Read("Views", "FileShelfPanel.xaml");
        var code = Read("Views", "FileShelfPanel.xaml.cs");
        Assert.Contains("x:Name=\"ShelfCard\"", page, StringComparison.Ordinal);
        Assert.Contains("Width=\"360\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialog", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHitTestVisible = false", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DestinationBox", panel, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"240\"", panel, StringComparison.Ordinal);
        Assert.Contains("ShellIconBinder.BindPath", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Toolbar_hover_and_drop_are_both_supported()
    {
        var toolbar = Read("Controls", "Toolbar", "AdaptiveCommandToolbar.xaml");
        var page = Read("Views", "NavigatorPage.Customization.cs");
        Assert.Contains("DragEnter=\"ShelfButton_DragOver\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("Drop=\"ShelfButton_Drop\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("_ = ShowShelfAsync();", page, StringComparison.Ordinal);
        Assert.Contains("await _shelfPanel.ReceiveDropAsync(e)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Drag_out_uses_deferred_windows_storage_items_and_keeps_copy_references()
    {
        var panel = Read("Views", "FileShelfPanel.xaml");
        var code = Read("Views", "FileShelfPanel.xaml.cs");
        Assert.Contains("CanDragItems=\"True\"", panel, StringComparison.Ordinal);
        Assert.Contains("SetDataProvider(StandardDataFormats.StorageItems", code, StringComparison.Ordinal);
        Assert.Contains("request.GetDeferral()", code, StringComparison.Ordinal);
        Assert.Contains("deferral.Complete()", code, StringComparison.Ordinal);
        Assert.Contains("DataPackageOperation.Copy | DataPackageOperation.Move", code, StringComparison.Ordinal);
        Assert.Contains("args.DropResult.HasFlag(DataPackageOperation.Move)", code, StringComparison.Ordinal);
        Assert.Contains("!Path.Exists(path)", code, StringComparison.Ordinal);
        Assert.Contains("!data.Properties.ContainsKey(DragMarker)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void File_area_waits_for_drop_completion_and_shelf_transfers_run_in_background()
    {
        var surface = Read("Controls", "FileSurface", "FileDetailsSurface.xaml.cs");
        var page = Read("Views", "NavigatorPage.Customization.cs");
        Assert.Contains("await performDrop(new FileDropRequest", surface, StringComparison.Ordinal);
        Assert.Contains("TransferShelfDropAsync", page, StringComparison.Ordinal);
        Assert.Contains("await FileShelfTransfer.RunAsync", page, StringComparison.Ordinal);
    }
}
