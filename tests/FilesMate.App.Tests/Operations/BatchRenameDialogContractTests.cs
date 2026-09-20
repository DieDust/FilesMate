using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Operations;

public sealed class BatchRenameDialogContractTests
{
    [Fact]
    public void Batch_rename_dialog_has_find_replace_and_virtualized_preview_hooks()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "BatchRenameDialog.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "BatchRenameDialog.xaml.cs"));
        Assert.Contains("FindBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ReplaceBox", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewList", xaml, StringComparison.Ordinal);
        Assert.Contains("BatchRenamePlanner.Plan", code, StringComparison.Ordinal);
    }

    [Fact]
    public void F2_routes_multi_selection_to_batch_rename()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("AppCommandId.BatchRename", code, StringComparison.Ordinal);
        Assert.Contains("ActiveSurface.Selection.Count > 1", code, StringComparison.Ordinal);
    }
}
