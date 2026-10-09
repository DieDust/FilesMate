using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Navigation;
using Microsoft.UI.Xaml;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private void InitializeRecursiveView()
    {
        Omni.LocalQueryChanged += (_, query) =>
        {
            if (!_disposed && !HomeLocation.IsHome(ViewModel.AddressText)) ViewModel.SetFilterQuery(query);
        };
        Commands.RecursiveViewRequested += async (_, enabled) =>
        {
            var pane = ViewModel;
            await pane.SetRecursiveEnabledAsync(enabled);
            if (!_disposed && enabled && pane.IsRecursive && ReferenceEquals(pane, ViewModel))
                pane.SetFilterQuery(Omni.LocalQuery);
            if (!_disposed) { SyncInlineFilters(); RefreshLayoutChrome(); ActiveSurface.Focus(FocusState.Programmatic); }
        };
        _fileActions.RecursiveSelection = () => ViewModel.IsRecursive;
        _fileActions.OperationSelectionPaths = () => ActiveSurface.SelectedOperationPaths();
        _fileActions.CaptureRecursiveOperationGuard = () => ActiveSurface.CaptureRecursiveOperationGuard();
        _fileActions.RecursiveSelectionAllowed = () => ActiveSurface.ValidateRecursiveSelection();
        _fileActions.RecursiveRootAllowed = () => ActiveSurface.ValidateRecursiveRoot();
    }

    private IReadOnlyList<string> SelectionForShelf()
    {
        if (ViewModel.IsRecursive)
        {
            ViewModel.ReportUserError(Localization.StringTable.Get("Recursive_ShelfUnavailable"));
            return [];
        }
        return SelectedPaths();
    }

    private async Task LeaveRecursiveForLayoutAsync(FileLayoutKind kind)
    {
        var pane = ViewModel;
        var surface = SurfaceOf(pane);
        await pane.SetRecursiveEnabledAsync(false);
        if (!_disposed) { surface.SetLayout(kind); surface.Focus(FocusState.Programmatic); }
    }

    private void ConfigureRecursiveSurface(FileDetailsSurface surface, int paneIndex)
    {
        PaneViewModel? Pane() => paneIndex == 2 ? _thirdVm : paneIndex == 1 ? _rightVm : _leftVm;
        if (Pane() is { } owner) owner.DeferRecursiveProgress = () => surface.DeferRecursiveProgress;
        surface.RecursiveLayoutRequested += async (_, kind) =>
        {
            if (Pane() is not { } pane) return;
            await pane.SetRecursiveEnabledAsync(false);
            if (!_disposed) surface.SetLayout(kind);
        };
        surface.RecursiveOperationBlocked += (_, message) => Pane()?.ReportUserError(message);
        surface.RecursiveToggleRequested += (_, id) =>
        {
            Pane()?.ToggleRecursiveNode(id);
            if (!_disposed) surface.Focus(FocusState.Programmatic);
        };
        surface.RecursiveOpenRequested += (_, id) =>
        {
            Pane()?.OpenRecursiveNode(id);
            if (!_disposed) surface.Focus(FocusState.Programmatic);
        };
        surface.RecursiveDepthRequested += (_, request) =>
        {
            Pane()?.SetRecursiveDepth(request.EntryIds, request.Depth);
            if (!_disposed) surface.Focus(FocusState.Programmatic);
        };
        surface.RecursiveAltDepthRequested += (_, request) =>
        {
            Pane()?.SetRecursiveDepth([request.EntryId], request.Depth, expandOnly: true);
            if (!_disposed) surface.Focus(FocusState.Programmatic);
        };
        surface.RecursiveDesiredExpanded = id => Pane()?.IsRecursiveNodeDesiredExpanded(id) == true;
        surface.RecursiveDesiredSubtreeExpanded = id => Pane()?.IsRecursiveSubtreeDesiredExpanded(id) == true;
        surface.RecursiveExpansionRequested += (_, request) =>
        {
            if (request.Expanded && !request.Subtree) Pane()?.OpenRecursiveNode(request.EntryId);
            else Pane()?.SetRecursiveExpanded(request.EntryId, request.Expanded, request.Subtree);
            if (!_disposed) surface.Focus(FocusState.Programmatic);
        };
        surface.CancelRecursiveScanRequested = () => Pane()?.CancelRecursiveScan() == true;
        surface.RecursiveCanContinue = () => Pane()?.CanContinueRecursiveScan == true;
        surface.RecursiveContinueRequested = () => Pane()?.ContinueRecursiveScan();
    }

    private void ApplyRecursivePresentation(PaneViewModel pane)
    {
        UpdateFolderStatus(pane);
        _applyingView.Add(pane);
        try { SurfaceOf(pane).SetRecursiveMode(pane.IsRecursive); }
        finally { _applyingView.Remove(pane); }
        if (ReferenceEquals(pane, ViewModel)) RefreshLayoutChrome();
    }
}
