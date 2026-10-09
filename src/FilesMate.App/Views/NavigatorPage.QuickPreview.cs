using FilesMate.App.Controls.Preview;
using FilesMate.App.Controls.FileSurface;
using Microsoft.UI.Xaml;
using FilesMate.Core.Diagnostics;

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
    internal bool InvokeWindowQuickPreview(long operation)
    {
        if (ActiveSurface.Selection.Count != 1 || PrimarySelectedPath() is null) return false;
        ToggleQuickPreview(operation); return true;
    }
    private void ToggleQuickPreview() => ToggleQuickPreview(0);
    private async void ToggleQuickPreview(long operation)
    {
        if (operation == 0) operation = PerformanceTrace.NextOperation();
        PerformanceTrace.Record(PerformanceStage.PreviewRequest, operation: operation, count: ActiveSurface.Selection.Count, auxiliary: 1);
        try
        {
        if (_quickPreview is { } current) { current.Close(); return; }
        if (!ActiveSurface.ValidateRecursiveSelection(FilesMate.Core.Recursive.RecursiveOperationScope.IdentityOnly)) return;
        if (ActiveSurface.Selection.Count != 1 || PrimarySelectedPath() is not { } path || App.WindowForElement(this) is not { } owner) return;
        var window = _quickPreview = new QuickPreviewWindow(owner);
        window.NavigateFile += (_, step) => ActiveSurface.MovePreviewSelection(step);
        _quickPreviewInput = new(XamlRoot.Content, CloseQuickPreview);
        window.Closed += (_, _) =>
        {
            if (_quickPreview == window) { _quickPreview = null; DetachQuickPreviewInput(); }
            if (_quickPreview is null && window.RestoreOwnerFocus && IsLoaded) ActiveSurface.Focus(FocusState.Programmatic);
        };
        window.Activate();
        PerformanceTrace.Record(PerformanceStage.PreviewWindowShown, operation: operation,
            outcome: PerformanceOutcome.Completed, auxiliary: WinRT.Interop.WindowNative.GetWindowHandle(window).ToInt64());
        await window.LoadAsync(path);
        PerformanceTrace.Record(PerformanceStage.PreviewContentLoaded, operation: operation, outcome: PerformanceOutcome.Completed);
        }
        catch (Exception error)
        {
            PerformanceTrace.Record(PerformanceStage.PreviewFailed, operation: operation, outcome: PerformanceOutcome.Failed, auxiliary: error.HResult);
            App.LogFailure("Quick preview", error);
            try { CloseQuickPreview(); } catch (Exception closeError) { App.LogFailure("Quick preview close", closeError); }
        }
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
        if (!ActiveSurface.ValidateRecursiveSelection(FilesMate.Core.Recursive.RecursiveOperationScope.IdentityOnly, report: false))
        { CloseQuickPreview(); return; }
        if (ActiveSurface.Selection.Count == 1 && PrimarySelectedPath() is { } path) _ = window.LoadAsync(path);
        else CloseQuickPreview();
    }
}
