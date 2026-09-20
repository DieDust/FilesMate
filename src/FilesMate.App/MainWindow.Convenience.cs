using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private static async Task RestoreNavigatorStateAsync(Views.NavigatorPage page, NavigatorTabContent tab, Navigation.ClosedTabState state)
    {
        try { await page.RestoreClosedTabAsync(state); }
        finally { tab.RestoreState = null; }
    }
    private DispatcherQueueTimer? _tabHoverTimer;
    private TabViewItem? _hoveredFileTab;

    private void ConfigureFileTabHover(TabViewItem tab)
    {
        tab.AllowDrop = true;
        tab.DragOver -= FileTab_DragOver;
        tab.DragOver += FileTab_DragOver;
        tab.DragLeave -= FileTab_DragLeave;
        tab.DragLeave += FileTab_DragLeave;
        tab.Drop -= FileTab_DragLeave;
        tab.Drop += FileTab_DragLeave;
        tab.Unloaded -= FileTab_Unloaded;
        tab.Unloaded += FileTab_Unloaded;
    }

    private void FileTab_DragOver(object sender, DragEventArgs e)
    {
        if (DraggedTab is not null || sender is not TabViewItem tab || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        // Hover only switches tabs; a transfer requires dropping into the destination pane.
        e.AcceptedOperation = DataPackageOperation.None;
        e.Handled = true;
        QueueFileTabHover(tab);
    }

    private void QueueFileTabHover(TabViewItem tab)
    {
        if (ReferenceEquals(tab, _hoveredFileTab)) return;
        CancelFileTabHover();
        if (ReferenceEquals(tab, Tabs.SelectedItem) || tab.Tag is not NavigatorTabContent) return;
        _hoveredFileTab = tab;
        if (_tabHoverTimer is null)
        {
            _tabHoverTimer = DispatcherQueue.CreateTimer();
            _tabHoverTimer.Interval = TimeSpan.FromMilliseconds(750);
            _tabHoverTimer.IsRepeating = false;
            _tabHoverTimer.Tick += (_, _) =>
            {
                var target = _hoveredFileTab;
                CancelFileTabHover();
                if (target is { IsLoaded: true } && Tabs.TabItems.Contains(target)) Tabs.SelectedItem = target;
            };
        }
        _tabHoverTimer.Start();
    }

    private void FileTab_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, _hoveredFileTab)) CancelFileTabHover();
    }

    private void FileTab_Unloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _hoveredFileTab)) CancelFileTabHover();
    }

    private void CancelFileTabHover()
    {
        _tabHoverTimer?.Stop();
        _hoveredFileTab = null;
    }
}
