using FilesMate.App.Commands;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Commands;

public sealed class ToolbarPresentationTests
{
    [Fact]
    public void Toolbar_shows_daily_file_commands_and_disables_them_without_a_target()
    {
        var none = ToolbarOverflowController.Present(
            CommandContext.ForToolbar(0, clipboardHasFiles: true));
        Assert.True(Find(none, ToolbarCommandId.New).Visible);
        Assert.True(Find(none, ToolbarCommandId.New).Enabled);
        Assert.True(Find(none, ToolbarCommandId.Paste).Visible);
        Assert.True(Find(none, ToolbarCommandId.Paste).Enabled);
        Assert.True(Find(none, ToolbarCommandId.Cut).Visible);
        Assert.False(Find(none, ToolbarCommandId.Cut).Enabled);
        Assert.True(Find(none, ToolbarCommandId.Copy).Visible);
        Assert.False(Find(none, ToolbarCommandId.Copy).Enabled);
        Assert.True(Find(none, ToolbarCommandId.Rename).Visible);
        Assert.False(Find(none, ToolbarCommandId.Rename).Enabled);
        Assert.False(Find(none, ToolbarCommandId.Share).Visible);
        Assert.True(Find(none, ToolbarCommandId.Delete).Visible);
        Assert.False(Find(none, ToolbarCommandId.Delete).Enabled);
        Assert.True(Find(none, ToolbarCommandId.Sort).Visible);
        Assert.True(Find(none, ToolbarCommandId.View).Visible);
        Assert.True(Find(none, ToolbarCommandId.More).Visible);
        Assert.False(Find(none, ToolbarCommandId.CopyPath).Visible);
        Assert.False(Find(none, ToolbarCommandId.CopyPath).Enabled);
        Assert.Equal("Copy path", Find(none, ToolbarCommandId.CopyPath).Label);
    }

    [Fact]
    public void Selection_enables_copy_path_and_rename_explains_multi_select()
    {
        var one = ToolbarOverflowController.Present(CommandContext.ForToolbar(1));
        Assert.True(Find(one, ToolbarCommandId.CopyPath).Visible);
        Assert.True(Find(one, ToolbarCommandId.CopyPath).Enabled);
        Assert.True(Find(one, ToolbarCommandId.Copy).Visible);
        Assert.True(Find(one, ToolbarCommandId.Copy).Enabled);
        Assert.True(Find(one, ToolbarCommandId.Rename).Visible);
        Assert.True(Find(one, ToolbarCommandId.Rename).Enabled);

        var many = ToolbarOverflowController.Present(CommandContext.ForToolbar(3));
        Assert.True(Find(many, ToolbarCommandId.CopyPath).Enabled);
        Assert.True(Find(many, ToolbarCommandId.Copy).Enabled);
        Assert.False(Find(many, ToolbarCommandId.Rename).Enabled);
        Assert.Equal("Cannot rename multiple items", Find(many, ToolbarCommandId.Rename).Tooltip);
    }

    [Fact]
    public void Overflow_hides_sort_then_view_and_then_more()
    {
        var wide = ToolbarOverflow.ForWidth(280);
        Assert.True(wide.ShowSort && wide.ShowView);
        Assert.False(wide.ShowMore);

        var medium = ToolbarOverflow.ForWidth(220);
        Assert.False(medium.ShowSort);
        Assert.True(medium.ShowView && medium.ShowMore);

        var narrow = ToolbarOverflow.ForWidth(160);
        Assert.False(narrow.ShowSort || narrow.ShowView);
        Assert.True(narrow.ShowMore);

        Assert.Equal(wide, ToolbarOverflow.ForWidth(480));
        Assert.Equal(medium, ToolbarOverflow.ForWidth(279));
    }

    [Fact]
    public void Toolbar_xaml_is_adaptive_and_does_not_ship_placeholder_commands()
    {
        var xaml = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Toolbar",
            "AdaptiveCommandToolbar.xaml"));
        Assert.Contains("FilesMate.Control.Height.Toolbar", xaml, StringComparison.Ordinal);
        Assert.Contains("CreateGroup", xaml, StringComparison.Ordinal);
        Assert.Contains("ClipboardGroup", xaml, StringComparison.Ordinal);
        Assert.Contains("OrganizeGroup", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewGroup", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviewButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FolderSizesButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE838;\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph=\"&#xE9E9;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Copy path", xaml, StringComparison.Ordinal);
        Assert.Contains("Placement=\"Bottom\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"RefreshItem\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Copy\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"New\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Cut\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Paste\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Delete\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);

        var sortStart = xaml.IndexOf("x:Name=\"SortButton\"", StringComparison.Ordinal);
        var folderSizesStart = xaml.IndexOf("x:Name=\"FolderSizesButton\"", StringComparison.Ordinal);
        var separatorStart = xaml.IndexOf("x:Name=\"ViewSeparator\"", StringComparison.Ordinal);
        var dualPaneStart = xaml.IndexOf("x:Name=\"DualPaneButton\"", StringComparison.Ordinal);
        Assert.True(sortStart >= 0 && folderSizesStart > sortStart && separatorStart > folderSizesStart);
        Assert.True(dualPaneStart > separatorStart);

        var detailsStart = xaml.IndexOf("x:Name=\"DetailsViewButton\"", StringComparison.Ordinal);
        var gridStart = xaml.IndexOf("x:Name=\"GridViewButton\"", StringComparison.Ordinal);
        Assert.True(detailsStart >= 0 && gridStart > detailsStart);
        Assert.Contains("Glyph=\"&#xF0E3;\"", xaml[detailsStart..gridStart], StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xF0E2;\"", xaml[gridStart..], StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        Assert.Contains("AdaptiveCommandToolbar", page, StringComparison.Ordinal);
        Assert.DoesNotContain("controls:CommandToolbar", page, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Toolbar",
            "AdaptiveCommandToolbar.xaml.cs"));
        Assert.Contains("ToolbarOverflow.ForWidth", code, StringComparison.Ordinal);
        Assert.Contains("ApplyContext", code, StringComparison.Ordinal);
        Assert.Contains("ToolbarOverflowController.Present", code, StringComparison.Ordinal);
        Assert.Contains("SetFolderSizesActive", code, StringComparison.Ordinal);
        Assert.Contains("FolderSizesClicked", code, StringComparison.Ordinal);
        Assert.Contains("FolderSizesLabel", code, StringComparison.Ordinal);
        Assert.Contains("Column_Size", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CanCopy", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new Button", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshClicked", code, StringComparison.Ordinal);

        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("Commands.ApplyContext", navigator, StringComparison.Ordinal);
        Assert.Contains("ToggleFolderSizes", navigator, StringComparison.Ordinal);
        Assert.Contains("SetFolderSizesActive", navigator, StringComparison.Ordinal);
        Assert.DoesNotContain("Commands.CanCopy", navigator, StringComparison.Ordinal);
    }

    private static ToolbarCommandState Find(IReadOnlyList<ToolbarCommandState> commands, ToolbarCommandId id) =>
        Assert.Single(commands, command => command.Id == id);
}
