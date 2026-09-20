using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class SettingsOverlayContractTests
{
    [Fact]
    public void Main_window_hosts_settings_as_a_glass_overlay_above_the_current_tab()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));

        Assert.Contains("DesktopAcrylicBackdrop", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsBackdrop\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemBackdropElement", xaml, StringComparison.Ordinal);
        Assert.Contains("<glass:FrostedBackdrop", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SettingsOverlay.ScrimBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsHost", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Text.PrimaryBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml"));
        Assert.Contains("x:Name=\"CloseButton\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"0,12,20,0\"", page, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"0,0,1,0\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_overlay_is_cached_and_created_on_a_low_priority_dispatcher_turn()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("SettingsPage? _settingsPage", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", code, StringComparison.Ordinal);
        Assert.Contains("SettingsHost.Content", code, StringComparison.Ordinal);
        Assert.Contains("SettingsOverlay.Visibility", code, StringComparison.Ordinal);
        Assert.Contains("CloseSettings", code, StringComparison.Ordinal);
        Assert.Contains("CloseRequested", code, StringComparison.Ordinal);
        Assert.Contains("SynchronizeOverlayTheme()", code, StringComparison.Ordinal);
        Assert.Contains("SettingsPanel.RequestedTheme", code, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.Tag is SettingsPage", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_material_has_theme_and_high_contrast_fallbacks()
    {
        var resources = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Themes",
            "AppThemeResources.xaml"));

        Assert.Contains("x:Key=\"FilesMate.SettingsOverlay.ScrimBrush\"", resources, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.SettingsPanel.BackgroundBrush\"", resources, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.SettingsNav.BackgroundBrush\"", resources, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"FilesMate.SettingsCard.BackgroundBrush\"", resources, StringComparison.Ordinal);
        Assert.Contains("AcrylicBrush", resources, StringComparison.Ordinal);
        Assert.Contains("HighContrast", resources, StringComparison.Ordinal);

        const string panelKey = "x:Key=\"FilesMate.SettingsPanel.BackgroundBrush\"";
        var start = 0;
        var found = 0;
        while (true)
        {
            var index = resources.IndexOf(panelKey, start, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            found++;
            var prefix = resources.Substring(Math.Max(0, index - 64), Math.Min(64, index));
            Assert.True(
                prefix.Contains("SolidColorBrush", StringComparison.Ordinal)
                || prefix.Contains("StaticResource", StringComparison.Ordinal),
                "Settings panel fallback is solid; HighContrast aliases system colors.");
            start = index + panelKey.Length;
        }

        Assert.True(found >= 3, "Light, Dark, and HighContrast must all define the settings panel fill.");
    }
}
