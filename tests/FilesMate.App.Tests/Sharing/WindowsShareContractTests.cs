using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Sharing;

public sealed class WindowsShareContractTests
{
    [Fact]
    public void Share_keeps_the_data_requested_handler_for_the_window_lifetime()
    {
        var share = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Sharing",
            "WindowsShareService.cs"));
        var app = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml.cs"));

        Assert.Contains("Properties.Title", share, StringComparison.Ordinal);
        Assert.Contains("SetStorageItems", share, StringComparison.Ordinal);
        Assert.Contains("ShowShareUIForWindow", share, StringComparison.Ordinal);
        Assert.Contains("DataRequested += _handler", share, StringComparison.Ordinal);
        Assert.Contains("DataRequested -= _handler", share, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", share, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowId", share, StringComparison.Ordinal);
        Assert.Contains("new WindowsShareService(window.NativeHandle)", app, StringComparison.Ordinal);
        var window = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.ShellCompatibility.cs"));
        Assert.Contains("_shellHost?.Handle ?? WinRT.Interop.WindowNative.GetWindowHandle(this)", window, StringComparison.Ordinal);
        Assert.Contains("share.Dispose()", app, StringComparison.Ordinal);
    }
}
