using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Panes;

public sealed class NavigatorDualPaneContractTests
{
    [Fact]
    public void Dual_pane_is_wired_into_the_navigator_without_nesting_pages()
    {
        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        var toolbar = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Toolbar",
            "AdaptiveCommandToolbar.xaml"));
        var toolbarCode = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Toolbar",
            "AdaptiveCommandToolbar.xaml.cs"));

        Assert.Contains("WorkspaceSplitView", page, StringComparison.Ordinal);
        Assert.Contains("WorkspaceSplitView.LeftContent", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileSurface\"", page, StringComparison.Ordinal);
        Assert.Contains("DualPaneAccelerator", page, StringComparison.Ordinal);
        Assert.Contains("Key=\"S\"", page, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control,Shift\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DualPaneButton\"", toolbar, StringComparison.Ordinal);
        Assert.Contains("DualPaneClicked", toolbarCode, StringComparison.Ordinal);
        Assert.Contains("ToggleDualPane", code, StringComparison.Ordinal);
        Assert.Contains("EnsureRightPane", code, StringComparison.Ordinal);
        Assert.Contains("WorkspaceLayoutKind.Vertical", code, StringComparison.Ordinal);
        Assert.Contains("PersistDualPane", code, StringComparison.Ordinal);
        Assert.Contains("ExplorerPreferences.DualPane", code, StringComparison.Ordinal);
        Assert.Contains("SetDualPane(App.ExplorerPreferences.DualPane, persist: false)", code, StringComparison.Ordinal);
        Assert.Contains("SetDualPane(!_dualPane, persist: true)", code, StringComparison.Ordinal);
        var pane = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Navigation",
            "PaneViewModel.cs"));
        Assert.Contains("new WindowsDirectoryEnumerator()", code, StringComparison.Ordinal);
        Assert.Contains("new WindowsDirectoryWatcher()", code, StringComparison.Ordinal);
        Assert.Contains("ListenForChangesAsync", pane, StringComparison.Ordinal);
        Assert.Contains("LiveDirectoryEntry.TryRead", pane, StringComparison.Ordinal);
        Assert.Contains("WatchDebounce { get; } = TimeSpan.FromMilliseconds(200)", pane, StringComparison.Ordinal);

        var chrome = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml.cs"));
        Assert.Contains("IsTrailingPane", chrome, StringComparison.Ordinal);
        Assert.Contains("ApplyPaneShape", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("Root.Padding = IsDualPane", chrome, StringComparison.Ordinal);
        Assert.Contains("new Thickness(8, 0, 0, 8)", chrome, StringComparison.Ordinal);
        Assert.Contains("new Thickness(0, 0, 8, 8)", chrome, StringComparison.Ordinal);
    }
}
