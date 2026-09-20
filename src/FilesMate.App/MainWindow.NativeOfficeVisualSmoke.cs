#if FILESMATE_UI_TEST
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Preview;
using Microsoft.UI.Xaml;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunNativeOfficeVisualSmokeAsync()
    {
        var results = new List<object>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var path in Directory.GetFiles(@"D:\FilesMate\artifacts\office-fixtures").Where(NativeOfficePreview.CanHandle))
        {
            QuickPreviewWindow? card = null;
            try
            {
                card = new QuickPreviewWindow(this);
                card.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
                card.Activate();
                await card.LoadAsync(path);
                var pane = (PreviewPane)typeof(QuickPreviewWindow).GetField("_pane", flags)!.GetValue(card)!;
                var session = (NativeOfficePreview?)typeof(PreviewPane).GetField("_nativeOffice", flags)!.GetValue(pane);
                if (session is null) throw new Exception("Native session missing");
                var cardHandle = WinRT.Interop.WindowNative.GetWindowHandle(card);
                if (GetOfficeOwner(session.Window, 4) != cardHandle)
                    throw new Exception("Native surface owned by a hidden window instead of the preview card");
                await Task.Delay(1200);
                // Capture the actual provider surface, not RenderTargetBitmap (which
                // excludes native windows), and reject the former flat covered surface.
                var colors = 0;
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    colors = CaptureNativeOfficeSurface(session.Window, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(path) + ".bmp"));
                    if (colors >= 16) break;
                    await Task.Delay(150);
                }
                if (colors < 16) throw new Exception("Office surface is blank: " + colors + " colors");
                GetOfficeWindowRect(session.Window, out var before);
                card.AppWindow.Move(new Windows.Graphics.PointInt32(-9700, -9800));
                await Task.Delay(250);
                GetOfficeWindowRect(session.Window, out var moved);
                if (moved.Left - before.Left != 300 || moved.Top - before.Top != 200) throw new Exception("Native surface did not follow owner move");
                card.AppWindow.Resize(new Windows.Graphics.SizeInt32(card.AppWindow.Size.Width + 100, card.AppWindow.Size.Height + 60));
                await Task.Delay(250);
                GetOfficeWindowRect(session.Window, out var resized);
                if (resized.Right - resized.Left <= moved.Right - moved.Left) throw new Exception("Native surface did not resize");
                // Activating the owner is what happens when grabbing a resize grip.
                // Earlier geometry-only checks never detected a popup behind it.
                card.Activate();
                for (var step = 0; step < 24; step++)
                {
                    var delta = step < 12 ? 12 : -12;
                    card.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-9700 + step * 2, -9800 + step,
                        card.AppWindow.Size.Width + delta, card.AppWindow.Size.Height + delta));
                    await Task.Delay(25);
                }
                await Task.Delay(400);
                if (GetOfficeOwner(session.Window, 4) != cardHandle || !IsOfficeAbove(session.Window, cardHandle))
                    throw new Exception("Native surface fell behind owner after activation/continuous resize");
                var resizeColors = CaptureNativeOfficeSurface(session.Window, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(path) + "-resized.bmp"));
                if (resizeColors < 16) throw new Exception("Resized native surface blank");
                typeof(PreviewPane).GetMethod("InfoTab_Click", flags)!.Invoke(pane, [pane, new RoutedEventArgs()]);
                await Task.Delay(150);
                if (IsWindowVisible(session.Window)) throw new Exception("Native surface covers information tab");
                typeof(PreviewPane).GetMethod("ContentTab_Click", flags)!.Invoke(pane, [pane, new RoutedEventArgs()]);
                await Task.Delay(150);
                card.AppWindow.Hide();
                await Task.Delay(150);
                if (IsWindowVisible(session.Window)) throw new Exception("Native surface remained after owner hidden");
                card.AppWindow.Show(false);
                await Task.Delay(250);
                if (!IsWindowVisible(session.Window)) throw new Exception("Native surface not restored with owner");
                var restoredColors = 0;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    restoredColors = CaptureNativeOfficeSurface(session.Window, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(path) + "-restored.bmp"));
                    if (restoredColors >= 16) break;
                    await Task.Delay(150);
                }
                if (restoredColors < 16) throw new Exception("Restored native surface blank");
                var pid = session.ProcessId;
                card.Close();
                await Task.Delay(2200);
                try { using var worker = System.Diagnostics.Process.GetProcessById(pid); if (!worker.HasExited) throw new Exception("Worker retained on close"); }
                catch (ArgumentException) { }
                results.Add(new { File = Path.GetFileName(path), Colors = colors, ResizeColors = resizeColors, OwnerAndZOrderVerified = true, Passed = true });
            }
            catch (Exception error) { results.Add(new { File = Path.GetFileName(path), Passed = false, Error = error.ToString() }); }
            finally { card?.Close(); }
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-office-visual.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsOfficeAbove(nint window, nint owner)
    {
        for (var next = GetOfficeOwner(window, 2); next != 0; next = GetOfficeOwner(next, 2))
            if (next == owner) return true;
        return false;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindow")] private static extern nint GetOfficeOwner(nint window, uint command);

    private static int CaptureNativeOfficeSurface(nint window, string path)
    {
        GetClientRect(window, out var size);
        var width = size.Right; var height = size.Bottom;
        if (width < 1 || height < 1) return 0;
        var dc = GetOfficeDC(window);
        var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, width, height);
        var old = SelectOfficeObject(memory, bitmap);
        try
        {
            if (!PrintOfficeWindow(window, memory, 2)) throw new Exception("Native capture failed");
            SelectOfficeObject(memory, old);
            var info = new OfficeBitmapInfo { Size = 40, Width = width, Height = height, Planes = 1, Bits = 32 };
            var pixels = new byte[width * height * 4];
            if (GetOfficeDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != height) throw new Exception("Native pixels unavailable");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write((ushort)0x4D42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54);
                writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(pixels);
            }
            var colors = new HashSet<int>();
            // Exclude outer edges, which could otherwise count borders as content.
            for (var y = height / 8; y < height * 7 / 8; y += 3)
                for (var x = width / 8; x < width * 7 / 8; x += 3)
                { var offset = (y * width + x) * 4; colors.Add(pixels[offset] | pixels[offset + 1] << 8 | pixels[offset + 2] << 16); }
            return colors.Count;
        }
        finally { SelectOfficeObject(memory, old); DeleteOfficeObject(bitmap); DeleteOfficeDC(memory); ReleaseOfficeDC(window, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct OfficeBitmapInfo { public int Size, Width, Height; public ushort Planes, Bits; public int Compression, ImageSize, XPels, YPels, Used, Important; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetOfficeWindowRect(nint window, out NativePreviewRect rect);
    [DllImport("user32.dll", EntryPoint = "PrintWindow")] private static extern bool PrintOfficeWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetDC")] private static extern nint GetOfficeDC(nint window);
    [DllImport("user32.dll", EntryPoint = "ReleaseDC")] private static extern int ReleaseOfficeDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll", EntryPoint = "SelectObject")] private static extern nint SelectOfficeObject(nint dc, nint value);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool DeleteOfficeObject(nint value);
    [DllImport("gdi32.dll", EntryPoint = "DeleteDC")] private static extern bool DeleteOfficeDC(nint dc);
    [DllImport("gdi32.dll", EntryPoint = "GetDIBits")] private static extern int GetOfficeDIBits(nint dc, nint bitmap, uint start, uint count, byte[] pixels, ref OfficeBitmapInfo info, uint usage);
}
#endif
