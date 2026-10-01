using System.Runtime.InteropServices;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace FilesMate.App.Input;

// The OLE drag loop pumps native messages; DispatcherQueue timers may stop
// ticking there. Track only a file drag that XAML has actually accepted.
internal sealed class ExternalDragFeedback : IDisposable
{
    private const nuint TimerId = 0x464D4446;
    private readonly nint _window;
    private readonly FrameworkElement _root;
    private readonly Func<NavigatorPage?> _page;
    private readonly Procedure _procedure;
    private readonly LegacyArchiveDragFeedback _legacy;
    private IReadOnlyList<string> _paths = [];
    private DataPackageOperation _allowed;
    private FileDetailsSurface? _surface;
    private bool _active;

    internal ExternalDragFeedback(nint window, FrameworkElement root, Func<NavigatorPage?> page)
    {
        _window = window; _root = root; _page = page;
        _procedure = WindowMessage;
        if (!SetWindowSubclass(window, _procedure, TimerId, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        _legacy = new LegacyArchiveDragFeedback(() => Begin([], DataPackageOperation.Copy), End);
    }

    internal void Begin(IReadOnlyList<string> paths, DataPackageOperation allowed)
    {
        if ((GetAsyncKeyState(1) & 0x8000) == 0) return;
        _paths = paths;
        _allowed = allowed;
        if (!_active) { _active = true; SetTimer(_window, TimerId, 16, 0); }
        Update();
    }

    internal bool TryPoint(out Point point)
    {
        point = default;
        if (!_root.IsLoaded || !GetPhysicalCursorPos(out var cursor) || !ScreenToClient(_window, ref cursor)) return false;
        var scale = _root.XamlRoot.RasterizationScale;
        point = new Point(cursor.X / scale, cursor.Y / scale);
        return true;
    }

    private void Update()
    {
        if (!_active) return;
        if ((GetAsyncKeyState(1) & 0x8000) == 0 || (GetAsyncKeyState(0x1B) & 0x8000) != 0)
        { End(); return; }
        if (!TryPoint(out var point)) { End(); return; }
        GetPhysicalCursorPos(out var screen);
        var hit = WindowFromPoint(screen);
        if (GetAncestor(hit, 2) != _window) { _surface?.ClearExternalDragFeedback(); _surface = null; return; }
        var next = _page()?.ExternalDragSurfaceAt(point);
        if (!ReferenceEquals(_surface, next)) _surface?.ClearExternalDragFeedback();
        _surface = next;
        next?.UpdateExternalDragFeedback(point, _paths, _allowed,
            (GetAsyncKeyState(0x11) & 0x8000) != 0, (GetAsyncKeyState(0x10) & 0x8000) != 0);
    }

    internal void End()
    {
        if (_active) KillTimer(_window, TimerId);
        _active = false;
        _surface?.ClearExternalDragFeedback(); _surface = null; _paths = [];
    }

    private nint WindowMessage(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        if (message == 0x0113 && wparam == TimerId)
        {
            try { Update(); } catch (Exception error) { End(); App.LogFailure("ExternalDragFeedback", error); }
            return 0;
        }
        return DefSubclassProc(window, message, wparam, lparam);
    }

    public void Dispose() { End(); _legacy.Dispose(); RemoveWindowSubclass(_window, _procedure, TimerId); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    private delegate nint Procedure(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint window, Procedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Procedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint window, nuint id, uint interval, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint window, nuint id);
    [DllImport("user32.dll")] private static extern bool GetPhysicalCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}
