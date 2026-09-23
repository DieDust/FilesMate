#if FILESMATE_UI_TEST
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunTitleBarMenuSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new PointInt32(80, 80));
            Activate();
            if (Content is FrameworkElement root) root.RequestedTheme = ElementTheme.Dark;
            SynchronizeTitleBarTheme();
            await Task.Delay(150);

            var location = AppWindow.Position;
            var x = location.X + 300;
            var y = location.Y + 20;
            var packed = (uint)(x & 0xffff) | ((uint)(y & 0xffff) << 16);
            SendMessageW(NativeHandle, WmNcRightButtonUp, 2, unchecked((nint)(int)packed));
            for (var i = 0; i < 40 && _titleBarMenuShowCount == 0; i++) await Task.Delay(50);
            result["NativeCaptionClickHandled"] = _titleBarMenuShowCount == 1;
            result["UsesFilesMateMenuStyle"] = _titleBarMenuHasAppStyle;
            result["UsesDarkTheme"] = _titleBarMenuTheme == ElementTheme.Dark;
            result["Passed"] = _titleBarMenuShowCount == 1 && _titleBarMenuHasAppStyle
                && _titleBarMenuTheme == ElementTheme.Dark;
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "titlebar-menu-smoke.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
#endif
