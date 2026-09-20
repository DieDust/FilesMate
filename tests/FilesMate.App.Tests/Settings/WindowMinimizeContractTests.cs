using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class WindowMinimizeContractTests
{
    [Fact]
    public void Minimized_window_does_not_update_caption_metrics_or_persist_placement()
    {
        var source = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        var layout = source[source.IndexOf("private void UpdateNonClientRegions()", StringComparison.Ordinal)..];
        layout = layout[..layout.IndexOf("private void AddTabStripPassthrough", StringComparison.Ordinal)];
        Assert.Contains("IsIconic(", layout, StringComparison.Ordinal);
        Assert.Contains("!double.IsFinite(scale) || scale <= 0", layout, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0, AppWindow.TitleBar.RightInset)", layout, StringComparison.Ordinal);
        Assert.Contains("double.IsFinite(captionWidth)", layout, StringComparison.Ordinal);

        var placement = source[source.IndexOf("private void PersistPlacement()", StringComparison.Ordinal)..];
        placement = placement[..placement.IndexOf("private void NewTabAccelerator", StringComparison.Ordinal)];
        Assert.Contains("IsIconic(", placement, StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_named_event_redirect_returns_before_sdk_fallback()
    {
        var source = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "AppLifecycle.cs"));
        var redirect = source[source.IndexOf("private static void RedirectActivationTo(", StringComparison.Ordinal)..];
        var signaled = redirect.IndexOf("if (incoming.Set())", StringComparison.Ordinal);
        var returned = redirect.IndexOf("return;", signaled, StringComparison.Ordinal);
        var sdkFallback = redirect.IndexOf("instance.RedirectActivationToAsync(args)", StringComparison.Ordinal);

        Assert.True(signaled >= 0);
        Assert.True(returned > signaled && returned < sdkFallback);
    }

    [Fact]
    public void Foreground_activation_does_not_restore_an_already_maximized_window()
    {
        var source = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "AppLifecycle.cs"));
        var foreground = source[source.IndexOf("public static void TryBringToForeground(", StringComparison.Ordinal)..];
        foreground = foreground[..foreground.IndexOf("private static void Instance_Activated", StringComparison.Ordinal)];
        Assert.Contains("if (IsIconic(hwnd))", foreground, StringComparison.Ordinal);
        Assert.Contains("ShowWindow(hwnd, SwRestore)", foreground, StringComparison.Ordinal);
        Assert.Contains("SetForegroundWindow(hwnd)", foreground, StringComparison.Ordinal);
    }
}
