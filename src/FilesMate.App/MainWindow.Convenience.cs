using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Views;
using FilesMate.Core.Operations;
using Windows.ApplicationModel.DataTransfer.DragDrop;

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
        tab.Drop -= FileTab_Drop;
        tab.Drop += FileTab_Drop;
        tab.Unloaded -= FileTab_Unloaded;
        tab.Unloaded += FileTab_Unloaded;
    }

    private void FileTab_DragOver(object sender, DragEventArgs e)
    {
        if (DraggedTab is not null || e.DataView.Contains(TabTransferPayload.Format)
            || sender is not TabViewItem tab || !DeviceTransferUI.HasFiles(e.DataView)) return;
        var destination = FileTabDestination(tab);
        var operation = TabFileOperation(e.DataView, destination,
            e.Modifiers.HasFlag(DragDropModifiers.Control), e.Modifiers.HasFlag(DragDropModifiers.Shift));
        e.AcceptedOperation = operation;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsCaptionVisible = operation != DataPackageOperation.None;
        if (operation != DataPackageOperation.None)
            e.DragUIOverride.Caption = StringTable.Get(operation == DataPackageOperation.Move ? "Drag_MoveTo" : "Drag_CopyTo")
                + " " + LocationCaption.Title(destination!);
        e.Handled = true;
        QueueFileTabHover(tab);
    }

    private static string? FileTabDestination(TabViewItem tab)
    {
        if (tab.Tag is not NavigatorTabContent { IsDisposed: false } content) return null;
        if (content.RestoreState is { } restored)
            return restored.ThirdActive && restored.Third is { } third ? third.Path : restored.RightActive && restored.Right is { } right ? right.Path : restored.Left.Path;
        if (content.Navigator is { } page) return page.ViewModel.CanReceiveFiles ? page.ViewModel.AddressText : null;
        return content.RequestedPath;
    }

    private static DataPackageOperation TabFileOperation(DataPackageView data, string? destination, bool control, bool shift,
        IReadOnlyList<string>? actualPaths = null)
    {
        if (string.IsNullOrWhiteSpace(destination)) return DataPackageOperation.None;
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(destination, out _)
            || data.Contains(DeviceTransferUI.ClipboardFormat))
            return !shift && (Path.IsPathFullyQualified(destination)
                || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(destination, out _))
                ? DataPackageOperation.Copy : DataPackageOperation.None;
        return FileDropPolicy.ResolveOperation(actualPaths ?? FileDropRequest.SourcePaths(data), destination, true, control, shift) switch
        {
            FileDropOperation.Move => DataPackageOperation.Move,
            FileDropOperation.Copy => DataPackageOperation.Copy,
            _ => DataPackageOperation.None,
        };
    }

    private async void FileTab_Drop(object sender, DragEventArgs e)
    {
        if (DraggedTab is not null || e.DataView.Contains(TabTransferPayload.Format)
            || sender is not TabViewItem tab || !DeviceTransferUI.HasFiles(e.DataView)) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.None;
        CancelFileTabHover();
        var destination = FileTabDestination(tab);
        var deferral = e.GetDeferral();
        try
        {
            e.AcceptedOperation = await DropFilesOnTabAsync(tab, e.DataView, destination,
                e.Modifiers.HasFlag(DragDropModifiers.Control), e.Modifiers.HasFlag(DragDropModifiers.Shift));
        }
        catch (Exception error) { App.LogFailure("FileTabDrop", error); }
        finally { deferral.Complete(); }
    }

    private async Task<DataPackageOperation> DropFilesOnTabAsync(TabViewItem tab, DataPackageView data,
        string? destination, bool control, bool shift)
    {
        if (_windowClosed || !Tabs.TabItems.Contains(tab) || !DeviceTransferUI.HasFiles(data)) return DataPackageOperation.None;
        var paths = await DeviceTransferUI.ReadItemsAsync(data);
        var operation = TabFileOperation(data, destination, control, shift, paths);
        if (paths.Length == 0 || operation == DataPackageOperation.None
            || _windowClosed || !Tabs.TabItems.Contains(tab)) return DataPackageOperation.None;
        // Freeze the folder before asynchronous data retrieval; loading/switching a tab
        // must not redirect a drop into a different directory.
        Tabs.SelectedItem = tab;
        if (tab.Tag is not NavigatorTabContent content) return DataPackageOperation.None;
        ScheduleNavigatorLoad(tab, content);
        for (var i = 0; i < 200 && (content.Navigator?.IsLoaded != true || content.RestoreState is not null); i++)
        {
            if (_windowClosed || content.IsDisposed || content.Load.State is LazyTabLoadState.Failed or LazyTabLoadState.Cancelled)
                return DataPackageOperation.None;
            await Task.Delay(25);
        }
        if (content.Navigator is not { IsLoaded: true } page || content.RestoreState is not null
            || content.IsDisposed || _windowClosed || !Tabs.TabItems.Contains(tab)) return DataPackageOperation.None;
        await page.ReceiveTabFileDropAsync(new FileDropRequest(paths, destination, operation,
            data.Properties.ContainsKey(FileDropRequest.ShelfMarker), control));
        return operation;
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
