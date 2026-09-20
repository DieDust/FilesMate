using FilesMate.App.Navigation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private ShellCompatibilityWindow? _shellHost;
    internal nint NativeHandle => _shellHost?.Handle ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
    internal nint ShellViewHandle => _shellHost?.ViewHandle ?? 0;
    public new AppWindow AppWindow => _shellHost?.AppWindow ?? base.AppWindow;
    public new UIElement Content { get => _shellHost?.Content ?? base.Content; set => base.Content = value; }
    public new SystemBackdrop? SystemBackdrop
    {
        get => base.SystemBackdrop;
        set { if (_shellHost is { } host) host.SetBackdrop(value); else base.SystemBackdrop = value; }
    }
    public new void Activate() { if (_shellHost is { } host) host.Activate(); else base.Activate(); }
    public new void Close()
    {
        // The native AppWindow is not the hidden WinUI Window. Dispose its
        // island while the dispatcher is alive, before closing the WinUI owner.
        if (_shellHost is not null) CleanupWindow();
        base.Close();
    }

    private void InitializeShellCompatibility()
    {
        if (!Program.UseNativeShellHost) return;
        var content = base.Content;
        var backdrop = base.SystemBackdrop;
        base.SystemBackdrop = null;
        base.Content = null;
        try
        {
            _shellHost = new ShellCompatibilityWindow(content);
            _shellHost.SetBackdrop(backdrop);
            _shellHost.Activated += () => DispatcherQueue.TryEnqueue(OnWindowActivated);
            _shellHost.CloseRequested += RequestCloseAfterFileWork;
        }
        catch
        {
            _shellHost?.Dispose();
            _shellHost = null;
            base.Content = content;
            base.SystemBackdrop = backdrop;
            throw;
        }
    }
}
