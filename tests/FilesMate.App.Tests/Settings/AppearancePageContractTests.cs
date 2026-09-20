using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class AppearancePageContractTests
{
    [Fact]
    public void Settings_shell_hosts_appearance_on_a_token_surface()
    {
        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml"));
        Assert.Contains("x:Name=\"CategoryList\"", page, StringComparison.Ordinal);
        Assert.Contains("Appearance", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SettingsNavItemStyle", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CloseButton\"", page, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Center\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SectionHost\"", page, StringComparison.Ordinal);
        Assert.Contains("Width=\"240\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.Pane.Gutter", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LiquidGlassSurface", page, StringComparison.Ordinal);
        Assert.Contains("files-folders", page, StringComparison.Ordinal);
        Assert.Contains("keyboard", page, StringComparison.Ordinal);
        Assert.Contains("advanced", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CommunityToolkit", page, StringComparison.Ordinal);
        Assert.DoesNotContain("later milestone", page, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml.cs"));
        Assert.Contains("new AppearancePage()", code, StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, UIElement>", code, StringComparison.Ordinal);
        Assert.Contains("CreateSection", code, StringComparison.Ordinal);
        Assert.Contains("ShowCategory(\"general\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Appearance_page_exposes_m1_options_and_theme_previews()
    {
        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "AppearancePage.xaml"));
        Assert.Contains("SettingCard", page, StringComparison.Ordinal);
        Assert.Contains("RequestedTheme=\"Light\"", page, StringComparison.Ordinal);
        Assert.Contains("RequestedTheme=\"Dark\"", page, StringComparison.Ordinal);
        Assert.Contains("ThemeChipButtonStyle", page, StringComparison.Ordinal);
        Assert.Contains("Width=\"96\"", page, StringComparison.Ordinal);
        Assert.Contains("Height=\"56\"", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ThemePreview.LightFill", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ThemePreview.DarkFill", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeSystem\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeLight\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeDark\"", page, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Text.SecondaryBrush", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Settings", "SettingCard.xaml")), StringComparison.Ordinal);
        Assert.Contains("FilesMate.Text.SecondaryBrush", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "FileSurfaceStyles.xaml")), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BackdropBox\"", page, StringComparison.Ordinal);
        Assert.Contains("ComboBoxForegroundPointerOver", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "AppThemeResources.xaml")), StringComparison.Ordinal);
        var combo = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "FileSurfaceStyles.xaml"));
        Assert.Contains("TargetType=\"ComboBox\"", combo, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ComboBox.BackgroundBrush", combo, StringComparison.Ordinal);
        Assert.Contains("FilesMate.ComboBox.BorderBrush", combo, StringComparison.Ordinal);
        Assert.Contains("Acrylic", page, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Mica\"", page, StringComparison.Ordinal);
        Assert.Contains("Tag=\"MicaAlt\"", page, StringComparison.Ordinal);
        Assert.Contains("Solid", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AccentHost\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StatusBarToggle\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolbarToggle\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlassEffectBox\"", page, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Off\"", page, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Balanced\"", page, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Immersive\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ReduceMotionBox\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ErrorText\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("=\"#", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CommunityToolkit", page, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "AppearancePage.xaml.cs"));
        Assert.Contains("AppearanceSettingsViewModel", code, StringComparison.Ordinal);
        Assert.Contains("SetThemeAsync", code, StringComparison.Ordinal);
        Assert.Contains("SetGlassEffectAsync", code, StringComparison.Ordinal);
        Assert.Contains("GlassEffectBalanced", code, StringComparison.Ordinal);
        Assert.Contains("button.BorderBrush", code, StringComparison.Ordinal);
        Assert.DoesNotContain("button.Background", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Glass.SurfaceBrush", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Chrome_and_sidebar_consume_appearance_without_holding_the_page()
    {
        var app = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml.cs"));
        Assert.Contains("AppearanceSettingsService", app, StringComparison.Ordinal);
        Assert.Contains("ApplyAppearance", app, StringComparison.Ordinal);
        var ctor = app.IndexOf("public App()", StringComparison.Ordinal);
        var launched = app.IndexOf("OnLaunched", StringComparison.Ordinal);
        var body = app[ctor..launched];
        Assert.True(
            body.IndexOf("ApplyApplicationTheme", StringComparison.Ordinal)
            < body.IndexOf("InitializeComponent", StringComparison.Ordinal));
        Assert.Contains("ApplicationTheme.Light", app, StringComparison.Ordinal);
        Assert.Contains("SystemThemeResolver.ResolveApplicationTheme()", app, StringComparison.Ordinal);
        Assert.Contains("ColorValuesChanged", app, StringComparison.Ordinal);
        Assert.Contains("CompositeAnimationSettings", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new SettingsPage", app, StringComparison.Ordinal);

        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("ApplyAppearance", window, StringComparison.Ordinal);
        Assert.Contains("DesktopAcrylicBackdrop", window, StringComparison.Ordinal);
        Assert.Contains("MicaBackdrop", window, StringComparison.Ordinal);
        Assert.Contains("MicaKind.BaseAlt", window, StringComparison.Ordinal);
        Assert.Contains("RequestedTheme", window, StringComparison.Ordinal);
        Assert.Contains("SystemThemeResolver.ResolveElementTheme()", window, StringComparison.Ordinal);
        Assert.Contains("BackdropKind.Solid", window, StringComparison.Ordinal);
        var windowXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        Assert.Contains("{ThemeResource FilesMate.App.SolidBackgroundBrush}", windowXaml, StringComparison.Ordinal);
        Assert.Contains("SolidShellRootStyle", window, StringComparison.Ordinal);

        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        Assert.Contains("AppearanceChanged", navigator, StringComparison.Ordinal);
        Assert.Contains("ShowStatusBar", navigator, StringComparison.Ordinal);
        Assert.Contains("CommandBarRow", navigator, StringComparison.Ordinal);

        var chrome = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml.cs"));
        Assert.Contains("ShowStatusBar", chrome, StringComparison.Ordinal);

        var sidebar = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Navigation",
            "NavigationSidebar.xaml"));
        Assert.Contains("x:Name=\"SettingsButton\"", sidebar, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void Accent_paints_theme_dictionaries_without_reloading_the_window_chrome()
    {
        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("ThemeDictionaries", window, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Item.SelectedBrush", window, StringComparison.Ordinal);
        Assert.Contains("ToggleSwitchFillOn", window, StringComparison.Ordinal);
        Assert.Contains("HighContrast", window, StringComparison.Ordinal);
        Assert.Contains("RequestedTheme != theme", window, StringComparison.Ordinal);
        Assert.Contains("_appliedBackdrop != effectiveBackdrop", window, StringComparison.Ordinal);
        Assert.Contains("_appliedGlass != settings.GlassEffect", window, StringComparison.Ordinal);
        Assert.DoesNotContain("PaintBrush(", window, StringComparison.Ordinal);
    }
}
