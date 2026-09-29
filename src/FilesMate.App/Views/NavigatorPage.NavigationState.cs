using FilesMate.App.Navigation;
using FilesMate.App.Services;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private sealed record NavigationViewport(double Offset, string[] SelectedNames);
    private sealed record PendingNavigationViewport(string Path, long Generation, NavigationViewport Viewport);
    private readonly Dictionary<PaneViewModel, Dictionary<string, NavigationViewport>> _navigationViewports = [];
    private readonly Dictionary<PaneViewModel, PendingNavigationViewport> _pendingNavigationViewports = [];

    private void Navigation_Navigating(object? sender, NavigationIntent intent)
    {
        var vm = ReferenceEquals(sender, _leftVm.Navigation) ? _leftVm
            : ReferenceEquals(sender, _rightVm?.Navigation) ? _rightVm
            : ReferenceEquals(sender, _thirdVm?.Navigation) ? _thirdVm : null;
        if (_disposed || vm is null) return;
        if (!_navigationViewports.TryGetValue(vm, out var locations))
            _navigationViewports[vm] = locations = new(StringComparer.OrdinalIgnoreCase);
        var surface = SurfaceOf(vm);
        // The controller still exposes the folder being left, and its entries
        // have not been cleared. Do not overwrite a saved position with a view
        // that is still loading or waiting for its own restoration.
        if (!_restoringClosedTab && !_pendingNavigationViewports.ContainsKey(vm)
            && !vm.IsLoading && vm.Navigation.CurrentPath is { } path
            && vm.Store is not null && vm.ViewIndex is { } index && surface.IsBoundTo(vm.Store, index))
        {
            locations.Remove(path);
            locations[path] = new(surface.ScrollOffset, surface.SelectedNames());
            if (locations.Count > 256) locations.Remove(locations.Keys.First());
        }
        _pendingNavigationViewports.Remove(vm);
        if (!_restoringClosedTab && locations.TryGetValue(intent.Path, out var saved))
            _pendingNavigationViewports[vm] = new(intent.Path, intent.Generation, saved);
    }

    private void TryRestoreNavigationViewport(PaneViewModel vm)
    {
        if (_disposed || !_pendingNavigationViewports.TryGetValue(vm, out var pending)) return;
        if (vm.Navigation.CurrentGeneration != pending.Generation
            || !string.Equals(vm.Navigation.CurrentPath, pending.Path, StringComparison.OrdinalIgnoreCase))
        {
            _pendingNavigationViewports.Remove(vm);
            return;
        }
        if (!IsLoaded || vm.IsLoading || _restoringViews.ContainsKey(vm) || _applyingView.Contains(vm)
            || vm.ViewIndex is not { } index || index.Generation != pending.Generation || index.Sort != vm.Sort)
            return;
        var surface = SurfaceOf(vm);
        if (!surface.IsBoundTo(vm.Store, index)) return;
        _pendingNavigationViewports.Remove(vm);
        // An explicit reveal (search result, newly created file, etc.) takes priority.
        if (ReferenceEquals(vm, _pendingSelectPane) && _pendingSelectPath is { Length: > 0 } selected
            && string.Equals(FolderCustomizationStore.Key(Path.GetDirectoryName(selected)),
                FolderCustomizationStore.Key(pending.Path), StringComparison.OrdinalIgnoreCase)) return;
        surface.RestoreSelectedNames(pending.Viewport.SelectedNames);
        surface.RestoreScrollOffset(pending.Viewport.Offset);
    }
}
