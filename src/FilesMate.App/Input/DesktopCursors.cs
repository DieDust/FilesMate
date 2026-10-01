using System.Runtime.InteropServices;

using Microsoft.UI.Input;

namespace FilesMate.App.Input;

/// <summary>
/// Keep semantic cursor shapes in XAML and display the user's native Windows pointers.
/// </summary>
internal static class DesktopCursors
{
    public static InputSystemCursor Arrow { get; } = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    public static InputSystemCursor SizeWestEast { get; } = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    public static InputSystemCursor SizeNorthSouth { get; } = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    public static InputSystemCursor Hand { get; } = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    internal static bool ApplyNativeCursor(InputCursor? cursor)
    {
        if (cursor is not InputSystemCursor system) return false;
        var id = system.CursorShape switch
        {
            InputSystemCursorShape.Arrow => 32512,
            InputSystemCursorShape.IBeam => 32513,
            InputSystemCursorShape.Wait => 32514,
            InputSystemCursorShape.Cross => 32515,
            InputSystemCursorShape.UpArrow => 32516,
            InputSystemCursorShape.SizeNorthwestSoutheast => 32642,
            InputSystemCursorShape.SizeNortheastSouthwest => 32643,
            InputSystemCursorShape.SizeWestEast => 32644,
            InputSystemCursorShape.SizeNorthSouth => 32645,
            InputSystemCursorShape.SizeAll => 32646,
            InputSystemCursorShape.UniversalNo => 32648,
            InputSystemCursorShape.Hand => 32649,
            InputSystemCursorShape.AppStarting => 32650,
            InputSystemCursorShape.Help => 32651,
            _ => 0,
        };
        if (id == 0) return false;
        // Shared system handles retain the active cursor scheme, native resolution and animation.
        // A copied bitmap cursor can be scaled again when it enters a high-DPI XAML island.
        var handle = LoadCursorW(0, id);
        if (handle == 0) return false;
        SetCursor(handle);
        return true;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LoadCursorW(nint instance, nint name);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetCursor(nint cursor);
}
