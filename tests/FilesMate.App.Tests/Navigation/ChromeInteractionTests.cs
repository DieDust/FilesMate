using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Navigation;

public sealed class ChromeInteractionTests
{
    [Fact]
    public void Title_and_tabs_use_compact_metrics_and_a_quiet_selected_surface()
    {
        var tokens = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "DesignTokens.xaml"));
        var tabs = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabBarStyles.xaml"));

        Assert.Contains("FilesMate.Control.Height.TitleBar\">48", tokens, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.Height\">38", tokens, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Tab.SelectedBrush", tabs, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ComboBox.BorderBrush", tabs, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.LiquidGlass.FillBrush", tabs, StringComparison.Ordinal);
        Assert.DoesNotContain("TabViewItemHeaderBackgroundSelected", tabs, StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_and_tab_buttons_use_style_owned_hit_sizes_without_tree_scans()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"BrandIcon\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"20\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"20\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeTabHitTargets", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TabButtonHitSize", code, StringComparison.Ordinal);

        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "TabStyles.xaml"));
        Assert.Contains("TabViewItemHeaderCloseButtonWidth", styles, StringComparison.Ordinal);
        Assert.Contains("TabViewItemHeaderCloseButtonHeight", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void System_caption_buttons_are_explicitly_synchronized_with_the_actual_theme()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("AppWindowTitleBar.IsCustomizationSupported()", code, StringComparison.Ordinal);
        Assert.Contains("root.ActualThemeChanged += Root_ActualThemeChanged", code, StringComparison.Ordinal);
        Assert.Contains("SynchronizeTitleBarTheme();", code, StringComparison.Ordinal);
        Assert.Contains("ButtonForegroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonBackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonHoverForegroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonHoverBackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonPressedForegroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonPressedBackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonInactiveForegroundColor", code, StringComparison.Ordinal);
        Assert.Contains("ButtonInactiveBackgroundColor", code, StringComparison.Ordinal);
        Assert.Contains("AppTitleBar.RequestedTheme", code, StringComparison.Ordinal);
        Assert.Contains("PaintTabHeaders", code, StringComparison.Ordinal);
        Assert.Contains("TitleBarTheme.Light", code, StringComparison.Ordinal);
        Assert.Contains("ForegroundColor", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_selection_visuals_are_rounded_and_bound_to_model_state()
    {
        var xaml = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Navigation",
            "NavigationSidebar.xaml"));
        var code = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Navigation",
            "NavigationSidebar.xaml.cs"));

        Assert.Contains("x:Name=\"SelectionBackground\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"{StaticResource FilesMate.Corner.Small}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"{x:Bind Selected", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"SelectionAccent\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FindName(\"SelectionBackground\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindName(\"SelectionAccent\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Canceling_omnibar_mode_restores_focus_to_the_file_surface()
    {
        var omnibar = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Omnibar",
            "Omnibar.xaml.cs"));
        var navigatorXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        var navigatorCode = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));

        Assert.Contains("ModeCanceled", omnibar, StringComparison.Ordinal);
        Assert.Contains("ModeCanceled=\"Omni_ModeCanceled\"", navigatorXaml, StringComparison.Ordinal);
        Assert.Contains("ActiveSurface.Focus(FocusState.Programmatic)", navigatorCode, StringComparison.Ordinal);
    }
}
