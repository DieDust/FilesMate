using System.Runtime.InteropServices;
using FilesMate.App.Localization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private const uint WmNcRightButtonUp = 0x00A5;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmSysCommand = 0x0112;
    private const nuint TitleBarSubclassId = 0x464D;
    private const nuint ScSize = 0xF000;
    private const nuint ScMove = 0xF010;
    private const nuint ScMinimize = 0xF020;
    private const nuint ScMaximize = 0xF030;
    private const nuint ScClose = 0xF060;
    private const nuint ScRestore = 0xF120;
    private TitleBarSubclassProcedure? _titleBarSubclass;
    private nint _titleBarSubclassHandle;
#if FILESMATE_UI_TEST
    private int _titleBarMenuShowCount;
    private bool _titleBarMenuHasAppStyle;
    private ElementTheme _titleBarMenuTheme;
#endif

    private void InstallTitleBarMenuHook()
    {
        var handle = NativeHandle;
        _titleBarSubclass = TitleBarMessage;
        if (SetWindowSubclass(handle, _titleBarSubclass, TitleBarSubclassId, 0))
            _titleBarSubclassHandle = handle;
        else
            App.LogFailure("TitleBarMenuHook", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }

    private void RemoveTitleBarMenuHook()
    {
        if (_titleBarSubclassHandle == 0 || _titleBarSubclass is null) return;
        RemoveWindowSubclass(_titleBarSubclassHandle, _titleBarSubclass, TitleBarSubclassId);
        _titleBarSubclassHandle = 0;
        _titleBarSubclass = null;
    }

    private nint TitleBarMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmNcDestroy) _titleBarSubclassHandle = 0;
        // DefWindowProc turns this caption click into the legacy system menu.
        // The tab items remain XAML passthrough regions with their own menus.
        if (message == WmNcRightButtonUp && wParam == 2 && !_windowClosed)
        {
            var packed = lParam.ToInt64();
            var screen = new NativePoint(unchecked((short)packed), unchecked((short)(packed >> 16)));
            if (DispatcherQueue.TryEnqueue(() =>
                {
                    try { ShowThemedTitleBarMenu(window, screen); }
                    catch (Exception error) { App.LogFailure("TitleBarMenu", error); }
                })) return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowThemedTitleBarMenu(nint window, NativePoint screen)
    {
        if (_windowClosed || AppTitleBar.XamlRoot is not { } root) return;
        var client = screen;
        if (!ScreenToClient(window, ref client)) return;
        var scale = root.RasterizationScale;
        if (!double.IsFinite(scale) || scale <= 0) return;
        var origin = AppTitleBar.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var position = new Point(
            Math.Clamp(client.X / scale - origin.X, 0, Math.Max(0, AppTitleBar.ActualWidth)),
            Math.Clamp(client.Y / scale - origin.Y, 0, Math.Max(0, AppTitleBar.ActualHeight)));

        var menu = new MenuFlyout { AreOpenCloseAnimationsEnabled = false };
        if (TryStyle("FilesMate.MenuFlyoutPresenterStyle", out var presenter))
        {
            var themedPresenter = new Style { TargetType = typeof(MenuFlyoutPresenter), BasedOn = presenter };
            themedPresenter.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, AppTitleBar.ActualTheme));
            menu.MenuFlyoutPresenterStyle = themedPresenter;
        }
        var state = AppWindow.Presenter as OverlappedPresenter;
        var maximized = state?.State == OverlappedPresenterState.Maximized;
        AddWindowCommand(menu, window, "TitleBar_Restore", ScRestore, maximized);
        AddWindowCommand(menu, window, "TitleBar_Move", ScMove, !maximized);
        AddWindowCommand(menu, window, "TitleBar_Size", ScSize, !maximized && state?.IsResizable != false);
        AddWindowCommand(menu, window, "TitleBar_Minimize", ScMinimize, state?.IsMinimizable != false);
        AddWindowCommand(menu, window, "TitleBar_Maximize", ScMaximize, !maximized && state?.IsMaximizable != false);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddWindowCommand(menu, window, "TitleBar_Close", ScClose, true, "Alt+F4");
        menu.ShowAt(AppTitleBar, new FlyoutShowOptions { Position = position });
#if FILESMATE_UI_TEST
        _titleBarMenuShowCount++;
        _titleBarMenuHasAppStyle = menu.MenuFlyoutPresenterStyle is not null;
        _titleBarMenuTheme = AppTitleBar.ActualTheme;
#endif
    }

    private void AddWindowCommand(MenuFlyout menu, nint window, string label, nuint command, bool enabled, string? accelerator = null)
    {
        var item = new MenuFlyoutItem { Text = StringTable.Get(label), IsEnabled = enabled };
        if (accelerator is not null) item.KeyboardAcceleratorTextOverride = accelerator;
        if (TryStyle("FilesMate.MenuFlyoutItemStyle", out var style)) item.Style = style;
        item.Click += (_, _) =>
        {
            if (!_windowClosed) PostMessageW(window, WmSysCommand, command, 0);
        };
        menu.Items.Add(item);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x; public int Y = y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint TitleBarSubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint window, TitleBarSubclassProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint window, TitleBarSubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
