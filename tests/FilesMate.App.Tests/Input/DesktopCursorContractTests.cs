using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Input;

public sealed class DesktopCursorContractTests
{
    [Fact]
    public void Pointers_load_the_user_cursor_scheme_instead_of_winui_stock_sprites()
    {
        var cursors = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Input", "DesktopCursors.cs"));
        Assert.Contains("LoadCursorW", cursors, StringComparison.Ordinal);
        Assert.Contains("CreateFromHCursor", cursors, StringComparison.Ordinal);
        Assert.Contains("32512", cursors, StringComparison.Ordinal);
        Assert.Contains("32644", cursors, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        Assert.Contains("input:ShellRoot", window, StringComparison.Ordinal);

        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("DesktopCursors.SizeWestEast", navigator, StringComparison.Ordinal);
        Assert.DoesNotContain("InputSystemCursor.Create", navigator, StringComparison.Ordinal);

        var details = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FileDetailsSurface.Columns.cs"));
        Assert.Contains("DesktopCursors.SizeWestEast", details, StringComparison.Ordinal);
        Assert.DoesNotContain("InputSystemCursor.Create", details, StringComparison.Ordinal);
    }
}
