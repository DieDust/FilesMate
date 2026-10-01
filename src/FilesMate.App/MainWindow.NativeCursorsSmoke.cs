#if FILESMATE_UI_TEST
using System.Runtime.InteropServices;
using FilesMate.App.Input;
using Microsoft.UI.Content;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task VerifyNativeCursorsAsync(Dictionary<string, object> report)
    {
        var original = TabHost.Content;
        var originalCursor = CursorGetCursor();
        var probe = new CursorProbeButton { Content = "Cursor resolution probe", HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch };
        var results = new List<object>();
        try
        {
            TabHost.Content = probe;
            await Task.Delay(150);
            probe.UpdateLayout();
            probe.Focus(FocusState.Programmatic);
            var target = BrowsingGetFocus();
            BrowsingGetWindowThreadProcessId(target, out var processId);
            Check(target != 0 && processId == Environment.ProcessId, "Cursor test input does not belong to this process");
            var binding = DesktopCursorBinding.Attach(probe) ?? throw new InvalidOperationException("Native cursor binding missing");
            var source = binding.Source;
            report["NativeCursorHost"] = new { Focus = target.ToInt64(), Bound = binding.Window.ToInt64(), Native = NativeHandle.ToInt64(),
                Environment = Microsoft.UI.Win32Interop.GetWindowFromWindowId(probe.XamlRoot.ContentIslandEnvironment.AppWindowId).ToInt64() };
            var point = probe.TransformToVisual(Content).TransformPoint(new Point(probe.ActualWidth / 2, probe.ActualHeight / 2));
            var scale = probe.XamlRoot.RasterizationScale;
            var packed = (nint)((int)Math.Round(point.X * scale) | ((int)Math.Round(point.Y * scale) << 16));
            foreach (var (shape, id) in new[] { (InputSystemCursorShape.Arrow, 32512), (InputSystemCursorShape.Hand, 32649),
                (InputSystemCursorShape.IBeam, 32513), (InputSystemCursorShape.SizeWestEast, 32644), (InputSystemCursorShape.SizeNorthSouth, 32645) })
            {
                var before = binding.Updates;
                probe.Pointer = InputSystemCursor.Create(shape);
                source.Cursor = InputSystemCursor.Create(shape);
                // Messages go only to this isolated test island; the physical mouse is never moved.
                CursorSendMessage(binding.Window, 0x0020, (nuint)binding.Window, (nint)((0x0200 << 16) | 1));
                await Task.Delay(60);
                var actual = CursorGetCursor();
                var expected = CursorLoadCursor(0, id);
                results.Add(new { Shape = shape.ToString(), Scale = scale, Updates = binding.Updates - before,
                    SemanticShape = (source.Cursor as InputSystemCursor)?.CursorShape.ToString(), SameNativeHandle = actual == expected,
                    Actual = CursorDimensions(actual), Expected = CursorDimensions(expected) });
                report["NativeCursorResolution"] = results;
                Check(binding.Updates > before, "Cursor message did not reach the binding");
                Check(source.Cursor is InputSystemCursor semantic && semantic.CursorShape == shape, "Cursor shape changed while routing native input");
                Check(actual == expected, "Cursor was copied or scaled instead of using the Windows handle: " + shape);
            }
            Check(DesktopCursors.Arrow is InputSystemCursor && DesktopCursors.Hand is InputSystemCursor, "Default cursors still use copied bitmap sprites");
            report["NativeCursorSchemeAndResolution"] = true;
        }
        finally
        {
            TabHost.Content = original;
            CursorSetCursor(originalCursor);
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }

    private sealed class CursorProbeButton : Button
    {
        internal InputCursor Pointer { set => ProtectedCursor = value; }
    }

    private static object CursorDimensions(nint cursor)
    {
        if (cursor == 0 || !CursorGetIconInfo(cursor, out var icon)) return new { Width = 0, Height = 0 };
        try
        {
            var color = icon.Color != 0;
            CursorGetObject(color ? icon.Color : icon.Mask, Marshal.SizeOf<CursorBitmap>(), out var bitmap);
            return new { Width = bitmap.Width, Height = color ? bitmap.Height : bitmap.Height / 2, icon.HotspotX, icon.HotspotY };
        }
        finally
        {
            if (icon.Color != 0) CursorDeleteObject(icon.Color);
            if (icon.Mask != 0) CursorDeleteObject(icon.Mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorIconInfo
    { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorBitmap
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("user32.dll", EntryPoint = "GetCursor")] private static extern nint CursorGetCursor();
    [DllImport("user32.dll", EntryPoint = "SetCursor")] private static extern nint CursorSetCursor(nint cursor);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern nint CursorLoadCursor(nint instance, nint name);
    [DllImport("user32.dll", EntryPoint = "GetIconInfo")] private static extern bool CursorGetIconInfo(nint cursor, out CursorIconInfo info);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int CursorGetObject(nint bitmap, int bytes, out CursorBitmap value);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool CursorDeleteObject(nint value);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint CursorSendMessage(nint window, uint message, nuint wparam, nint lparam);
}
#endif
