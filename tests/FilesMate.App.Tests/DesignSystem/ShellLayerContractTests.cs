using System.Xml.Linq;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class ShellLayerContractTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Right_side_shell_has_omnibar_command_bar_and_file_pane_in_order()
    {
        var document = XDocument.Load(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var orderedNames = document
            .Descendants()
            .Select(element => element.Attribute(Xaml + "Name")?.Value)
            .Where(name => name is "OmnibarRow" or "CommandBarRow" or "FilePaneRow")
            .OfType<string>()
            .ToArray();

        Assert.Equal(["OmnibarRow", "CommandBarRow", "FilePaneRow"], orderedNames);
    }

    [Fact]
    public void Command_toolbar_is_not_a_child_of_the_omnibar_row()
    {
        var document = XDocument.Load(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var omnibarRow = document
            .Descendants()
            .Single(element => element.Attribute(Xaml + "Name")?.Value == "OmnibarRow");

        Assert.DoesNotContain(
            omnibarRow.Descendants(),
            element => element.Name.LocalName == "AdaptiveCommandToolbar");
    }

    [Fact]
    public void Shell_layers_use_semantic_surface_resources()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var filePane = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml"));

        Assert.Contains("FilesMate.CommandBarCardStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContentChrome\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.CommandBar.BackgroundBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.FileArea.BackgroundBrush", filePane, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.Pane.Gutter", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_shell_rows_use_grid_length_resources()
    {
        var tokens = XDocument.Load(Path.Combine(ThemeXaml.AppRoot, "Themes", "DesignTokens.xaml"));
        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var keyedResources = tokens.Root!
            .Elements()
            .ToDictionary(
                element => element.Attribute(Xaml + "Key")?.Value ?? string.Empty,
                element => element.Name.LocalName,
                StringComparer.Ordinal);

        Assert.Equal("GridLength", keyedResources["FilesMate.Row.Omnibar"]);
        Assert.Equal("GridLength", keyedResources["FilesMate.Row.CommandBar"]);
        Assert.Contains("FilesMate.Row.Omnibar", navigator, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Row.CommandBar", navigator, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_navigation_and_command_chrome_use_shared_liquid_glass_surfaces()
    {
        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        Assert.Contains("x:Name=\"TitleBarGlass\"", window, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind=\"Chrome\"", window, StringComparison.Ordinal);
        Assert.Contains("FilesMate.LiquidGlassSurfaceStyle", window, StringComparison.Ordinal);

        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        Assert.Contains("x:Name=\"SidebarHost\"", navigator, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind=\"Sidebar\"", navigator, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CommandBarCard\"", navigator, StringComparison.Ordinal);
        Assert.Contains("SurfaceKind=\"Command\"", navigator, StringComparison.Ordinal);

        var omnibar = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Omnibar",
            "Omnibar.xaml"));
        Assert.Contains("x:Name=\"PathHost\"", omnibar, StringComparison.Ordinal);
        Assert.Contains("glass:LiquidGlassSurface", omnibar, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAmbientSheenEnabled", omnibar, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_cards_use_the_reusable_glass_surface()
    {
        var style = File.ReadAllText(Path.Combine(ThemeXaml.ThemesRoot, "FileSurfaceStyles.xaml"));
        Assert.Contains("TargetType=\"glass:LiquidGlassSurface\"", style, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource FilesMate.LiquidGlassSurfaceStyle}\"", style, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SettingsCard.BackgroundBrush", style, StringComparison.Ordinal);

        string[] pages =
        [
            "AppearancePage.xaml",
            "GeneralPage.xaml",
            "ShortcutsSettingsPage.xaml",
            "TagManagementPage.xaml",
            "AboutPage.xaml",
        ];

        foreach (var page in pages)
        {
            var source = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", page));
            Assert.Contains("glass:LiquidGlassSurface", source, StringComparison.Ordinal);
            Assert.Contains("FilesMate.GlassCardStyle", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Menus_use_transient_acrylic_with_a_static_fallback()
    {
        var resources = File.ReadAllText(Path.Combine(ThemeXaml.ThemesRoot, "AppThemeResources.xaml"));
        Assert.Contains("FilesMate.Menu.BackgroundBrush", resources, StringComparison.Ordinal);
        Assert.Contains("AcrylicBrush", resources, StringComparison.Ordinal);
        Assert.Contains("FallbackColor", resources, StringComparison.Ordinal);

        var styles = File.ReadAllText(Path.Combine(ThemeXaml.ThemesRoot, "MenuStyles.xaml"));
        Assert.Contains("FilesMate.Menu.BackgroundBrush", styles, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource FilesMate.MenuFlyoutPresenterStyle}\"", styles, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource FilesMate.MenuFlyoutItemStyle}\"", styles, StringComparison.Ordinal);
    }
}
