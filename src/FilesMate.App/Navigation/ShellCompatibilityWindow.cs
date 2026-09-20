using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace FilesMate.App.Navigation;

/// <summary>Opt-in native host for clients that discover Shell views by HWND class.</summary>
internal sealed class ShellCompatibilityWindow : IDisposable
{
    private static readonly WindowProcedure Procedure = ProcessMessage;
    private static readonly Dictionary<nint, ShellCompatibilityWindow> Hosts = new();
    private static bool _registered;
    private readonly DesktopWindowXamlSource _source;
    private UIElement? _content;
    private bool _disposed;
    public nint Handle { get; }
    public nint ViewHandle { get; }
    public AppWindow AppWindow { get; }
    // The island content is assigned once. Activation/theme callbacks can run
    // while a popup changes focus; querying the native source then can block.
    public UIElement? Content => _content;
    public event Action? Activated;
    public event Action? CloseRequested;

    public ShellCompatibilityWindow(UIElement content)
    {
        RegisterClasses();
        Handle = CreateWindowExW(0, "CabinetWClass", "FilesMate", 0x00CF0000,
            unchecked((int)0x80000000), unchecked((int)0x80000000), 1280, 800, 0, 0, GetModuleHandleW(null), 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            ViewHandle = CreateWindowExW(0, "SHELLDLL_DefView", "", 0x50000000,
                0, 0, 1280, 800, Handle, 0, GetModuleHandleW(null), 0);
            if (ViewHandle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            AppWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Handle));
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _source = new DesktopWindowXamlSource();
            _source.Initialize(Win32Interop.GetWindowIdFromWindow(ViewHandle));
            _source.Content = content;
            _content = content;
            _source.TakeFocusRequested += Source_TakeFocusRequested;
            _source.SiteBridge.Show();
            Hosts.Add(Handle, this);
            ResizeContent();
        }
        catch
        {
            Hosts.Remove(Handle);
            _source?.Dispose();
            DestroyWindow(Handle);
            throw;
        }
    }

    public void SetBackdrop(Microsoft.UI.Xaml.Media.SystemBackdrop? backdrop) => _source.SystemBackdrop = backdrop;
    public void Activate()
    {
        ShowWindow(Handle, IsIconic(Handle) ? 9 : 5);
        SetForegroundWindow(Handle);
        Activated?.Invoke();
    }

    private void ResizeContent()
    {
        if (_disposed || _source.SiteBridge is not { } bridge || !GetClientRect(Handle, out var bounds)) return;
        var width = Math.Max(1, bounds.Right);
        var height = Math.Max(1, bounds.Bottom);
        SetWindowPos(ViewHandle, 0, 0, 0, width, height, 0x0014);
        bridge.MoveAndResize(new RectInt32(0, 0, width, height));
    }

    private void Source_TakeFocusRequested(DesktopWindowXamlSource sender,
        DesktopWindowXamlSourceTakeFocusRequestedEventArgs args)
    {
        // All focusable content lives in this island. Tab at either end wraps
        // inside it, instead of leaving keyboard focus on the empty native host.
        if (!_disposed && args.Request.Reason is XamlSourceFocusNavigationReason.First or XamlSourceFocusNavigationReason.Last)
            sender.NavigateFocus(new XamlSourceFocusNavigationRequest(args.Request.Reason));
    }

    private static nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (Hosts.TryGetValue(window, out var host))
        {
            try
            {
                if (message == 0x0005 && wParam != 1) host.ResizeContent(); // WM_SIZE except minimized
                if (message == 0x0007 && !host._source.HasFocus) // WM_SETFOCUS / task switch
                    host._source.NavigateFocus(new XamlSourceFocusNavigationRequest(XamlSourceFocusNavigationReason.Restore));
                if (message == 0x0006 && (wParam & 0xffff) != 0) host.Activated?.Invoke();
                if (message == 0x0010) { host.CloseRequested?.Invoke(); return 0; }
                if (message == 0x02E0) // WM_DPICHANGED
                {
                    var bounds = Marshal.PtrToStructure<NativeRect>(lParam);
                    SetWindowPos(window, 0, bounds.Left, bounds.Top, bounds.Right - bounds.Left,
                        bounds.Bottom - bounds.Top, 0x0014);
                    return 0;
                }
            }
            catch (Exception error) { App.LogFailure("ShellCompatibilityWindow", error); }
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _content = null;
        Hosts.Remove(Handle);
        _source.TakeFocusRequested -= Source_TakeFocusRequested;
        try { _source.Content = null; _source.Dispose(); }
        finally { DestroyWindow(Handle); }
    }

    private static void RegisterClasses()
    {
        if (_registered) return;
        foreach (var name in new[] { "CabinetWClass", "SHELLDLL_DefView" })
        {
            var definition = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = Procedure,
                Instance = GetModuleHandleW(null), Name = name,
                Cursor = LoadCursorW(0, 32512),
            };
            if (RegisterClassExW(ref definition) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _registered = true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; public nint SmallIcon;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string name, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
}
