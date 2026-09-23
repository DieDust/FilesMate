using FilesMate.App.Commands;
using FilesMate.App.Models;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task ShowEjectBlockersAsync(string root, IReadOnlyList<string> affectedRoots)
    {
        if (_disposed || !SafeDeviceEject.IsDriveRoot(root)) return;
        var roots = affectedRoots.Where(SafeDeviceEject.IsDriveRoot)
            .Append(root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var panel = new FileLockDialog();
        panel.ConfigureForDeviceEject(async () =>
        {
            HideLockOverlay();
            await DeviceEjectUI.ShowAsync(Sidebar, root);
        });
        panel.ReleasePreviewAsync = async () =>
        {
            panel.ReleaseIcons();
            await App.VacateFoldersAsync(roots);
        };
        _ = panel.SetPathsAsync(roots);
        await ShowLockOverlayAsync(panel);
    }

    private CommandContext DeviceCommandContext() => new(CommandSurface.Shortcut,
        ActiveSurface.Selection.Count, ActiveSurface.PrimaryIsDirectory(), false, ViewModel.CanRefresh,
        ViewModel.CanReceiveFiles, PaneFileActions.ClipboardHasFiles(), PrimarySelectedPath(),
        FolderPath: ViewModel.AddressText, OtherPanePath: OtherPaneDestination(_rightActive), IsPortableDevice: true);

    private IReadOnlyList<HomeSearchHit> SearchCurrentDeviceFolder(string query)
    {
        if (!ViewModel.IsPortableDevice || ViewModel.Store is not { } store) return [];
        return store.Observe(entries => entries.Where(e => e.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(100).Select(e => new HomeSearchHit(e.Name, ViewModel.FullPath(e), e.Kind == EntryKind.Directory)).ToArray());
    }

    private void DevicesChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        if (_leftVm.IsPortableDevice) _leftVm.Refresh();
        if (_rightVm?.IsPortableDevice == true) _rightVm.Refresh();
    }
}
