using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class SettingsRuntimeContractTests
{
    [Fact]
    public void Selection_change_cannot_touch_page_hosts_during_xaml_initialization()
    {
        var code = File.ReadAllText(
            Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("readonly bool _initialized", code, StringComparison.Ordinal);
        Assert.Contains("_initialized = true", code, StringComparison.Ordinal);
        Assert.Contains("if (!_initialized)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_creation_is_contained_by_the_page_factory_and_cached_in_the_overlay()
    {
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("IAppPageFactory", code, StringComparison.Ordinal);
        Assert.Contains("_pageFactory.Create(() => new SettingsPage())", code, StringComparison.Ordinal);
        Assert.Contains("PageLoadErrorPage", code, StringComparison.Ordinal);
        Assert.Contains("SettingsHost.Content", code, StringComparison.Ordinal);
        Assert.Contains("SettingsPage? _settingsPage", code, StringComparison.Ordinal);
    }
}
