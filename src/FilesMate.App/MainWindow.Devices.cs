using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private DeviceChangeSubscription? _deviceSubscription;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _deviceRefreshTimer;
    private void InitializeDevices()
    {
        _deviceRefreshTimer = DispatcherQueue.CreateTimer();
        _deviceRefreshTimer.Interval = TimeSpan.FromMilliseconds(700);
        _deviceRefreshTimer.IsRepeating = false;
        _deviceRefreshTimer.Tick += (_, _) =>
        {
            if (_windowClosed) return;
            PortableDeviceCatalog.Invalidate();
            App.NotifyDevicesChanged();
        };
        try { _deviceSubscription = new DeviceChangeSubscription(NativeHandle, () =>
        {
            _deviceRefreshTimer.Stop();
            _deviceRefreshTimer.Start();
        }, mask => DispatcherQueue.TryEnqueue(() =>
        {
            if (_windowClosed) return;
            foreach (var item in Tabs.TabItems.OfType<TabViewItem>())
                if (item.Tag is NavigatorTabContent { Navigator: { } page }) page.DeviceVolumesRemoved(mask);
        })); }
        catch (Exception error) { App.LogFailure("DeviceNotifications", error); }
    }

}
