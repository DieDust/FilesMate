using System.Runtime.InteropServices;
using FilesMate.App.Controls.FileSurface;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App.Input;

/// <summary>Consume list wheel messages before WinUI can start native pixel scrolling.</summary>
internal static class NativeListWheel
{
    internal static bool TryHandle(UIElement root, int delta, bool horizontal, nint position)
    {
        if (root.XamlRoot is not { } xamlRoot || delta == 0 || !root.IsHitTestVisible) return false;
        if (VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Any(popup => popup.IsOpen && popup.IsLightDismissEnabled)) return false;
        var packed = position.ToInt64();
        var point = new NativeWheelPoint { X = unchecked((short)packed), Y = unchecked((short)(packed >> 16)) };
        var window = Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        if (window == 0 || !ScreenToClient(window, ref point)) return false;
        var logical = new Point(point.X / xamlRoot.RasterizationScale, point.Y / xamlRoot.RasterizationScale);
        // Only the topmost hit can receive the wheel. Settings, menus and the sidebar
        // must not scroll a file surface behind them.
        for (DependencyObject? element = VisualTreeHelper.FindElementsInHostCoordinates(logical, root).FirstOrDefault();
            element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FileDetailsSurface surface)
                return surface.HandleListWheelFromWindow(root, logical, delta, horizontal);
        return false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeWheelPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window, ref NativeWheelPoint point);
}
