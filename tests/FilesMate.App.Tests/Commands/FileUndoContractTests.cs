using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Commands;

public sealed class FileUndoContractTests
{
    [Fact]
    public void File_mutations_record_undo_and_ctrl_z_is_wired()
    {
        var actions = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "PaneFileActions.cs"));
        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));

        Assert.Contains("App.FileUndo.Push", actions, StringComparison.Ordinal);
        Assert.Contains("FileUndoRecord.Recycled", actions, StringComparison.Ordinal);
        Assert.Contains("TryUndo", actions, StringComparison.Ordinal);
        Assert.Contains("TryRedo", actions, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"UndoAccelerator\"", navigator, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RedoAccelerator\"", navigator, StringComparison.Ordinal);
        Assert.Contains("FileAcceleratorsBlocked()", code, StringComparison.Ordinal);
        Assert.Contains("_fileActions.Undo()", code, StringComparison.Ordinal);
        Assert.Contains("_fileActions.Redo()", code, StringComparison.Ordinal);
    }
}
