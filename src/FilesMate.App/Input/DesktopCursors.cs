using System.Runtime.InteropServices;

using Microsoft.UI.Input;

using WinRT;

namespace FilesMate.App.Input;

/// <summary>
/// Pointers from the user's Windows cursor scheme.
/// WinUI <see cref="InputSystemCursor"/> draws stock sprites and ignores that scheme.
/// </summary>
internal static class DesktopCursors
{
    // winuser.h OEM resource ids (MAKEINTRESOURCE).
    private const int IdcArrow = 32512;
    private const int IdcSizeWestEast = 32644;
    private const int IdcSizeNorthSouth = 32645;

    private static InputCursor? _arrow;
    private static InputCursor? _sizeWestEast;
    private static InputCursor? _sizeNorthSouth;

    public static InputCursor Arrow => _arrow ??= Create(IdcArrow, InputSystemCursorShape.Arrow);

    public static InputCursor SizeWestEast =>
        _sizeWestEast ??= Create(IdcSizeWestEast, InputSystemCursorShape.SizeWestEast);

    public static InputCursor SizeNorthSouth =>
        _sizeNorthSouth ??= Create(IdcSizeNorthSouth, InputSystemCursorShape.SizeNorthSouth);

    private static InputCursor Create(int idc, InputSystemCursorShape fallback)
    {
        try
        {
            var handle = LoadCursorW(0, idc);
            if (handle != 0 && FromHandle(handle) is { } cursor)
            {
                return cursor;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        return InputSystemCursor.Create(fallback);
    }

    private static InputCursor? FromHandle(nint handle)
    {
        const string classId = "Microsoft.UI.Input.InputCursor";
        Marshal.ThrowExceptionForHR(WindowsCreateString(classId, classId.Length, out var hstring));
        try
        {
            var iid = typeof(IInputCursorStaticsInterop).GUID;
            var hr = RoGetActivationFactory(hstring, iid, out var factory);
            if (hr < 0 || factory is null)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(factory.CreateFromHCursor(handle, out var abi));
            if (abi == 0)
            {
                return null;
            }

            try
            {
                return MarshalInspectable<InputCursor>.FromAbi(abi);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }
        finally
        {
            _ = WindowsDeleteString(hstring);
        }
    }

    [ComImport]
    [Guid("ac6f5065-90c4-46ce-beb7-05e138e54117")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInputCursorStaticsInterop
    {
        public void GetIids(out int iidCount, out nint iids);

        public void GetRuntimeClassName(out nint className);

        public void GetTrustLevel(out int trustLevel);

        [PreserveSig]
        public int CreateFromHCursor(nint hcursor, out nint inputCursor);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LoadCursorW(nint hInstance, nint lpCursorName);

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RoGetActivationFactory(
        nint runtimeClassId,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IInputCursorStaticsInterop? factory);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WindowsCreateString(string? sourceString, int length, out nint hstring);

    [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WindowsDeleteString(nint hstring);
}
