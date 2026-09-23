using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Views;

/// <summary>One visible removal request, from preparation through the Windows result.</summary>
internal static class DeviceEjectUI
{
    private static bool _showing;

#if FILESMATE_UI_TEST
    internal static bool IsShowingForTest => _showing;
    internal static Func<string, Task<DeviceEjectTarget?>>? TestQuery;
    internal static Func<DeviceEjectTarget, Task<DeviceEjectResult>>? TestRequest;
#endif

    internal static async Task ShowAsync(FrameworkElement host, string path)
    {
        // A second click must not treat our own removal lease as a file transfer.
        // The existing dialog continues to show the state of the original request.
        if (_showing || !host.IsLoaded || host.XamlRoot is null) return;
        _showing = true;
        IDisposable? lifetime = null;
        Task? operation = null;
        var running = false;
        var inspectLocks = false;
        IReadOnlyList<string> blockerRoots = [path];
        var page = FindNavigatorPage(host);
        try
        {
            lifetime = FileOperationLifetime.TryBeginWhenIdle();
            running = lifetime is not null;
            var status = new TextBlock
            {
                Text = StringTable.Get(running ? "Device_EjectPreparing" : "Device_EjectBusy"),
                TextWrapping = TextWrapping.Wrap, MaxWidth = 440,
            };
            var progress = new ProgressBar { IsIndeterminate = true, Visibility = running ? Visibility.Visible : Visibility.Collapsed };
            var content = new StackPanel { Spacing = 16 };
            content.Children.Add(status);
            content.Children.Add(progress);
            // Capture the window's XamlRoot before vacating device tabs. The
            // initiating sidebar can unload while its device view is released.
            var dialog = new ContentDialog
            {
                XamlRoot = host.XamlRoot, Title = StringTable.Get("Sidebar_Eject"), Content = content,
                CloseButtonText = running ? string.Empty : StringTable.Get("Close"),
            };
            ContentDialogTheme.Apply(dialog, host);
            dialog.Closing += (_, args) => args.Cancel = running;
            dialog.Opened += (_, _) =>
            {
                if (running) operation = RemoveAsync();
            };
            async Task RemoveAsync()
            {
                try
                {
                    var target = await QueryAsync(path)
                        ?? throw new InvalidOperationException(StringTable.Get("Device_EjectUnavailable"));
                    if (target.AffectedRoots.Count > 0) blockerRoots = target.AffectedRoots;
                    await App.VacateFoldersAsync(target.AffectedRoots);
                    status.Text = StringTable.Get("Device_EjectRemoving");
                    var result = await RequestAsync(target);
                    if (result.Succeeded) PortableDeviceCatalog.Invalidate();
                    App.NotifyDevicesChanged();
                    status.Text = StringTable.Get(result.Succeeded ? "Device_EjectSuccess"
                        : result.BlockingUsbDebugging ? "Device_EjectDebugging" : "Device_EjectDenied");
                    if (!result.Succeeded)
                    {
                        App.LogFailure("SafeDeviceEject", new IOException($"Windows removal failed: CR={result.ErrorCode}, veto={result.Veto}, owner={result.BlockingName}"));
                        if (page is not null && !target.Optical && SafeDeviceEject.IsDriveRoot(target.Location))
                            dialog.PrimaryButtonText = StringTable.Get("Device_EjectShowBlockers");
                        if (result.Veto is 3 or 4 && !string.IsNullOrWhiteSpace(result.BlockingName))
                            status.Text += "\n" + result.BlockingName;
                        else if (!string.IsNullOrWhiteSpace(result.BlockingDeviceName))
                            status.Text += "\n" + StringTable.Format("Device_EjectBlockingInterface", result.BlockingDeviceName);
                    }
                }
                catch (Exception error)
                {
                    App.LogFailure("SafeDeviceEject", error);
                    status.Text = error.Message;
                }
                finally
                {
                    // Reading the result is not an active file operation.
                    lifetime?.Dispose();
                    lifetime = null;
                    running = false;
                    progress.IsIndeterminate = false;
                    progress.Visibility = Visibility.Collapsed;
                    dialog.CloseButtonText = StringTable.Get("Close");
                }
            }
            inspectLocks = await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            // Even if the host disappears, keep the native operation alive until
            // Windows returns. Closing the UI must not release an active lease.
            if (operation is not null) await operation;
            lifetime?.Dispose();
            _showing = false;
        }
        if (inspectLocks && page?.IsLoaded == true)
            await page.ShowEjectBlockersAsync(path, blockerRoots);
    }

    private static NavigatorPage? FindNavigatorPage(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is NavigatorPage page) return page;
        return null;
    }

    private static Task<DeviceEjectTarget?> QueryAsync(string path)
    {
#if FILESMATE_UI_TEST
        if (TestQuery is not null) return TestQuery(path);
#endif
        return SafeDeviceEject.QueryAsync(path);
    }

    private static Task<DeviceEjectResult> RequestAsync(DeviceEjectTarget target)
    {
#if FILESMATE_UI_TEST
        if (TestRequest is not null) return TestRequest(target);
#endif
        return SafeDeviceEject.RequestAsync(target);
    }
}
