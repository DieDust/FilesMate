using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Navigation;

public sealed class TabLifecycleContractTests
{
    [Fact]
    public void Closing_or_cancelling_a_tab_disposes_its_navigator_page()
    {
        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));

        Assert.Contains("navigator.TakeNavigatorForDisposal()", window, StringComparison.Ordinal);
        Assert.Contains("DisposeNavigatorAsync", window, StringComparison.Ordinal);
        Assert.Contains("_ = DisposeNavigatorAsync(page)", window, StringComparison.Ordinal);
        Assert.Contains("item.Tag = null", window, StringComparison.Ordinal);
        Assert.DoesNotContain("TryEnqueue(DispatcherQueuePriority.Low, async", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Navigator_page_has_an_idempotent_explicit_lifetime()
    {
        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));

        Assert.Contains("NavigatorPage : Page, IAsyncDisposable", navigator, StringComparison.Ordinal);
        Assert.Contains("public async ValueTask DisposeAsync()", navigator, StringComparison.Ordinal);
        Assert.Contains("_leftVm.PropertyChanged -= ViewModel_PropertyChanged", navigator, StringComparison.Ordinal);
        Assert.Contains("_leftVm.OpenFileRequested -= ViewModel_OpenFileRequested", navigator, StringComparison.Ordinal);
        Assert.Contains("Clipboard.ContentChanged -= Clipboard_ContentChanged", navigator, StringComparison.Ordinal);
        Assert.Contains("FileSurface.ReleaseResources()", navigator, StringComparison.Ordinal);
        Assert.Contains("await _leftVm.DisposeAsync()", navigator, StringComparison.Ordinal);
        Assert.Contains("if (_disposed)", navigator, StringComparison.Ordinal);
    }

    [Fact]
    public void File_pane_timer_is_stopped_and_unhooked_when_visual_is_unloaded()
    {
        var chrome = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "FileSurface",
            "FilePaneChrome.xaml.cs"));

        Assert.Contains("Unloaded += FilePaneChrome_Unloaded", chrome, StringComparison.Ordinal);
        Assert.Contains("_delay.Stop()", chrome, StringComparison.Ordinal);
        Assert.Contains("_delay.Tick -= Delay_Tick", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("_delay.Tick += (_, _)", chrome, StringComparison.Ordinal);
    }
}
