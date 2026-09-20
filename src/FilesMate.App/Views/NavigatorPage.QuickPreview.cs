using FilesMate.App.Controls.Preview;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private QuickPreviewWindow? _quickPreview;
    private void CloseQuickPreview() => _quickPreview?.Close();
    private async void ToggleQuickPreview()
    {
        if (_quickPreview is not null) { CloseQuickPreview(); return; }
        if (PrimarySelectedPath() is not { } path || App.WindowForElement(this) is not { } owner) return;
        SetPreviewVisible(false);
        var window = _quickPreview = new QuickPreviewWindow(owner);
        window.NavigateFile += (_, step) => ActiveSurface.MovePreviewSelection(step);
        window.Closed += (_, _) => { if (_quickPreview == window) _quickPreview = null; if(IsLoaded) ActiveSurface.Focus(FocusState.Programmatic); };
        window.Activate();
        await window.LoadAsync(path);
    }
    private void RefreshQuickPreview()
    {
        if (_quickPreview is not { } window) return;
        if (PrimarySelectedPath() is { } path) _ = window.LoadAsync(path);
        else CloseQuickPreview();
    }
}
