using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Input;

/// <summary>Keep native pointer resolution and route column wheel steps before XAML pixel scrolling.</summary>
internal sealed class DesktopCursorBinding
{
    private static readonly ConditionalWeakTable<ContentIsland, DesktopCursorBinding> Bindings = new();
    private readonly InputPointerSource _source;
    private readonly WeakReference<UIElement> _root;
    private readonly CursorWindowProcedure _procedure;
    private readonly HashSet<nint> _windows = [];
    private const nuint SubclassId = 0x464D4352;
#if FILESMATE_UI_TEST
    internal int Updates { get; private set; }
    internal InputPointerSource Source => _source;
    internal nint Window => _windows.FirstOrDefault();
#endif

    private DesktopCursorBinding(ContentIsland island, UIElement root)
    {
        _root = new(root.XamlRoot?.Content ?? root);
        _source = InputPointerSource.GetForIsland(island);
        _procedure = WindowMessage;
        var window = Win32Interop.GetWindowFromWindowId(island.Environment.AppWindowId);
        Install(window);
        EnumChildWindows(window, (child, _) => { Install(child); return true; }, 0);
        _source.PointerEntered += PointerChanged;
        _source.PointerMoved += PointerChanged;
        _source.PointerPressed += PointerChanged;
        _source.PointerReleased += PointerChanged;
        island.Closed += () =>
        {
            foreach (var child in _windows.ToArray()) RemoveWindowSubclass(child, _procedure, SubclassId);
            _windows.Clear();
            _source.PointerEntered -= PointerChanged;
            _source.PointerMoved -= PointerChanged;
            _source.PointerPressed -= PointerChanged;
            _source.PointerReleased -= PointerChanged;
            Bindings.Remove(island);
        };
    }

    internal static DesktopCursorBinding? Attach(UIElement element)
    {
#if FILESMATE_UI_TEST
        if (Environment.GetEnvironmentVariable("FILESMATE_DISABLE_NATIVE_CURSORS") == "1") return null;
#endif
        // Use the island that owns this XAML tree. A native host and its popups
        // can share an AppWindowId; looking up the first matching island is ambiguous.
        var island = element.XamlRoot?.ContentIsland;
        return island is null || island.IsClosed ? null : Bindings.GetValue(island, value => new(value, element));
    }

    private void Install(nint window)
    {
        if (window != 0 && SetWindowSubclass(window, _procedure, SubclassId, 0)) _windows.Add(window);
    }

    private nint WindowMessage(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        if (message == 0x0082) _windows.Remove(window); // WM_NCDESTROY
        try
        {
            if (message is 0x020A or 0x020E && (wparam & 8) == 0 && _root.TryGetTarget(out var root)
                && NativeListWheel.TryHandle(root, unchecked((short)(wparam >> 16)), message == 0x020E, lparam))
                return 0;
        }
        catch (Exception error) { App.LogFailure("NativeListWheel", error); }
        var result = DefSubclassProc(window, message, wparam, lparam);
        // XAML keeps choosing text/hand/resize shapes. At the end of native cursor dispatch,
        // replace its stock sprite with the corresponding shared Windows scheme handle.
        try
        {
            if (message == 0x0020 && (lparam.ToInt64() & 0xffff) == 1 && CursorMessagesEnabled && DesktopCursors.ApplyNativeCursor(_source.Cursor))
            {
#if FILESMATE_UI_TEST
                Updates++;
#endif
                return 1;
            }
        }
        catch (Exception error) { App.LogFailure("NativeCursor", error); }
        return result;
    }

    private void PointerChanged(InputPointerSource sender, PointerEventArgs args)
    {
        if (!CursorEventsEnabled) return;
        if (args.CurrentPoint.PointerDeviceType != PointerDeviceType.Mouse) return;
        if (DesktopCursors.ApplyNativeCursor(sender.Cursor))
        {
#if FILESMATE_UI_TEST
            Updates++;
#endif
        }
    }

#if FILESMATE_UI_TEST
    private static bool CursorMessagesEnabled => Environment.GetEnvironmentVariable("FILESMATE_SKIP_CURSOR_MESSAGES") != "1";
    private static bool CursorEventsEnabled => Environment.GetEnvironmentVariable("FILESMATE_SKIP_RAW_CURSOR_EVENTS") != "1";
#else
    private static bool CursorMessagesEnabled => true;
    private static bool CursorEventsEnabled => true;
#endif

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint CursorWindowProcedure(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool CursorEnumWindow(nint window, nint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, CursorWindowProcedure procedure, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, CursorWindowProcedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, CursorEnumWindow callback, nint data);
}
