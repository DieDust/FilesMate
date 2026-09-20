using FilesMate.App.Input;

using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Controls.Panes;

internal sealed class SplitThumb : Grid
{
    public SplitThumb()
    {
        ProtectedCursor = DesktopCursors.SizeWestEast;
    }

    public void SetHorizontal(bool horizontal) =>
        ProtectedCursor = horizontal
            ? DesktopCursors.SizeNorthSouth
            : DesktopCursors.SizeWestEast;
}
