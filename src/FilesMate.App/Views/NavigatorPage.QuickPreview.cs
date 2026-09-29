using FilesMate.App.Controls.Preview;
using FilesMate.App.Controls.FileSurface;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private QuickPreviewWindow? _quickPreview;
    private QuickPreviewDismissal? _quickPreviewInput;
    internal void CloseQuickPreview()
    {
        var window = _quickPreview;
        _quickPreview = null;
        DetachQuickPreviewInput();
        window?.Dismiss();
    }
    private async void ToggleQuickPreview()
    {
        if (_quickPreview is { } current) { current.Close(); return; }
        if (ActiveSurface.Selection.Count != 1 || PrimarySelectedPath() is not { } path || App.WindowForElement(this) is not { } owner) return;
        SetPreviewVisible(false);
        var window = _quickPreview = new QuickPreviewWindow(owner);
        window.NavigateFile += (_, step) => ActiveSurface.MovePreviewSelection(step);
        _quickPreviewInput = new(XamlRoot.Content, CloseQuickPreview);
        window.Closed += (_, _) =>
        {
            if (_quickPreview == window) { _quickPreview = null; DetachQuickPreviewInput(); }
            if (_quickPreview is null && window.RestoreOwnerFocus && IsLoaded) ActiveSurface.Focus(FocusState.Programmatic);
        };
        window.Activate();
        await window.LoadAsync(path);
    }

    private void DetachQuickPreviewInput()
    {
        _quickPreviewInput?.Dispose();
        _quickPreviewInput = null;
    }

    internal void DismissQuickPreviewOutside(DependencyObject? source, bool leftButton = true) =>
        _quickPreviewInput?.DismissOutside(source, leftButton);
    private void RefreshQuickPreview()
    {
        if (_quickPreview is not { } window) return;
        if (ActiveSurface.Selection.Count == 1 && PrimarySelectedPath() is { } path) _ = window.LoadAsync(path);
        else CloseQuickPreview();
    }
}
