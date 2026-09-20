using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Panes;

public sealed class WorkspaceSplitViewContractTests
{
    [Fact]
    public void Split_separator_supports_dragging_and_reset_without_rebuilding_pane_content()
    {
        var xaml = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Panes",
            "WorkspaceSplitView.xaml"));
        var code = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Panes",
            "WorkspaceSplitView.xaml.cs"));

        Assert.Contains("Separator_PointerPressed", xaml, StringComparison.Ordinal);
        Assert.Contains("Separator_PointerMoved", xaml, StringComparison.Ordinal);
        Assert.Contains("Separator_DoubleTapped", xaml, StringComparison.Ordinal);
        Assert.Contains("local:SplitThumb", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"6\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnSpacing=\"8\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"{ThemeResource FilesMate.Divider.Brush}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkspaceLayoutMath.ClampRatio", code, StringComparison.Ordinal);
        Assert.Contains("ResetSplitRatio", code, StringComparison.Ordinal);
        Assert.Contains("SplitterVisual", code, StringComparison.Ordinal);
        Assert.Contains("DesktopCursors.SizeWestEast", File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Panes",
            "SplitThumb.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("new NavigatorPage", code, StringComparison.Ordinal);
    }
}
