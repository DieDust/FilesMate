using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FilesMate.Platform.Windows.Shell;

/// <summary>UI-thread HWND subscription; callback only schedules a refresh.</summary>
public sealed class DeviceChangeSubscription : IDisposable
{
    private readonly nint _window;
    private readonly SubclassProc _procedure;
    private readonly Action _changed;
    private readonly Action<uint>? _volumesRemoved;
    private nint _registration;
    private bool _disposed;
    private const nuint SubclassId = 0x464d4456;

    public DeviceChangeSubscription(nint window, Action changed, Action<uint>? volumesRemoved = null)
    {
        _window = window;
        _changed = changed;
        _volumesRemoved = volumesRemoved;
        _procedure = ProcessMessage;
        if (!SetWindowSubclass(window, _procedure, SubclassId, 0)) throw new Win32Exception();
        var filter = new DeviceInterface { Size = Marshal.SizeOf<DeviceInterface>(), Type = 5,
            ClassGuid = new Guid(PortableDeviceLocation.InterfaceId) };
        _registration = RegisterDeviceNotificationW(window, ref filter, 0);
        if (_registration == 0) { Dispose(); throw new Win32Exception(); }
    }

    public static bool IsRefreshMessage(uint message, nuint change) => message == 0x0219
        && change is 0x0007 or 0x8000 or 0x8004; // topology, arrival, removal

    private nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (!_disposed && IsRefreshMessage(message, wParam))
        {
            try
            {
                // DEV_BROADCAST_VOLUME: fixed header followed by the drive-letter mask.
                if (wParam == 0x8004 && lParam != 0 && Marshal.ReadInt32(lParam) >= 20 && Marshal.ReadInt32(lParam, 4) == 2)
                    _volumesRemoved?.Invoke(unchecked((uint)Marshal.ReadInt32(lParam, 12)));
                _changed();
            }
            catch { /* Never unwind through a native window procedure. */ }
        }
        if (message == 0x0082) Dispose();
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registration != 0) { UnregisterDeviceNotification(_registration); _registration = 0; }
        RemoveWindowSubclass(_window, _procedure, SubclassId);
    }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterface { public int Size, Type, Reserved; public Guid ClassGuid; public short Name; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint RegisterDeviceNotificationW(nint window, ref DeviceInterface filter, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterDeviceNotification(nint handle);
}
