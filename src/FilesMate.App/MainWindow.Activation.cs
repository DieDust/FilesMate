using System.Runtime.InteropServices;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    internal bool CanReceiveActivation
    {
        get
        {
            if (_windowClosed) return false;
            try { return IsActivationWindow(NativeHandle); }
            catch (COMException) { return false; }
        }
    }

    internal void RetireUnavailableWindow() => CleanupWindow();

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsActivationWindow(nint hwnd);
}
