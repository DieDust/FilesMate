using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Input;

/// <summary>
/// Window content root that uses the user's default pointer instead of WinUI stock sprites.
/// </summary>
internal sealed class ShellRoot : Grid
{
    public ShellRoot()
    {
        ProtectedCursor = DesktopCursors.Arrow;
    }
}
