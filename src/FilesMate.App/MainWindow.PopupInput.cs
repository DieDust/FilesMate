using Microsoft.UI.Xaml.Controls.Primitives;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private readonly HashSet<FlyoutBase> _inputPopups = [];

    internal void SetPopupInputActive(FlyoutBase popup, bool active)
    {
        if (active) _inputPopups.Add(popup);
        else _inputPopups.Remove(popup);
        if (!_windowClosed) UpdateNonClientRegions();
    }
}
