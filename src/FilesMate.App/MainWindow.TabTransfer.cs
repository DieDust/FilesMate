using System.Runtime.InteropServices;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private Guid _lastReceivedTabTransfer;
    private EventWaitHandle? _tabTransferReceipt;
    private TabViewItem? _dragOperationTab;

    private static TabTransferPayload CreateTabTransfer(TabViewItem tab)
    {
        if (tab.Tag is Views.SearchResultsPage search)
            return new(1, Environment.ProcessId, Guid.NewGuid(), new(new(search.Request.Location,
                new FolderViewSettings(true, 2, Core.Entries.EntrySort.Name), 0)));
        var content = (NavigatorTabContent)tab.Tag;
        var state = content.RestoreState ?? content.Navigator?.CaptureClosedTab()
            ?? new ClosedTabState(new(content.RequestedPath ?? HomeLocation.Uri,
                new FolderViewSettings(true, 2, Core.Entries.EntrySort.Name), 0, History: content.InitialHistory));
        return new(1, Environment.ProcessId, Guid.NewGuid(), state);
    }

    private async Task ReceiveTabDropAsync(DragEventArgs e, int index)
    {
        if (!e.DataView.Contains(TabTransferPayload.Format)) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.None;
        var deferral = e.GetDeferral();
        try
        {
            if (_windowClosed) return;
            if (DraggedTab is { } tab && DragSource is { } source)
            {
                if (ReferenceEquals(source, this)) MoveTabWithin(tab, index);
                else MoveTabFrom(source, tab, index);
            }
            else
            {
                var payload = TabTransferPayload.Parse(await e.DataView.GetDataAsync(TabTransferPayload.Format) as string);
                if (payload is null || _windowClosed) return;
                // Open the source's one-use receipt before modifying the target.
                // Unlike a deferred DataPackage provider, reading formats cannot
                // accidentally acknowledge a transfer during drag negotiation.
                using var receipt = EventWaitHandle.OpenExisting(payload.ReceiptName);
                if (!ReceiveExternalTab(payload, index)) return;
                receipt.Set();
            }
            e.AcceptedOperation = DataPackageOperation.Move;
        }
        catch (Exception error) { App.LogFailure("ReceiveTab", error); }
        finally { HideTabInsertion(); deferral.Complete(); }
    }

    private void MoveTabWithin(TabViewItem tab, int insertionIndex)
    {
        var previous = Tabs.TabItems.IndexOf(tab);
        if (previous < 0) throw new InvalidOperationException("The source tab is no longer available.");
        var target = Math.Clamp(insertionIndex > previous ? insertionIndex - 1 : insertionIndex, 0, Tabs.TabItems.Count - 1);
        _handledTabDrop = true;
        if (previous == target) return;
        Tabs.TabItems.RemoveAt(previous);
        Tabs.TabItems.Insert(target, tab);
        Tabs.SelectedItem = tab;
    }

    private void MoveTabFrom(MainWindow source, TabViewItem tab, int index)
    {
        if (!source.Tabs.TabItems.Contains(tab)) throw new InvalidOperationException("The source tab is no longer available.");
        var originalIndex = source.Tabs.TabItems.IndexOf(tab);
        source.DetachTab(tab);
        try { AttachTab(tab, index); }
        catch
        {
            if (Tabs.TabItems.Contains(tab)) DetachTab(tab);
            source.AttachTab(tab, originalIndex);
            throw;
        }
        source._handledTabDrop = true;
        _ = source.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, source.CloseIfEmpty);
    }

    private bool ReceiveExternalTab(TabTransferPayload payload, int index)
    {
        if (payload.SourceProcessId == Environment.ProcessId || _lastReceivedTabTransfer == payload.Id) return false;
        var state = payload.State;
        if (FilesMate.Search.SearchPageRequest.TryParse(state.Left.Path, out var search))
        {
            OpenSearchPage(search);
            var searchTab = (TabViewItem)Tabs.SelectedItem;
            var from = Tabs.TabItems.IndexOf(searchTab);
            Tabs.TabItems.RemoveAt(from);
            Tabs.TabItems.Insert(Math.Clamp(index, 0, Tabs.TabItems.Count), searchTab);
            Tabs.SelectedItem = searchTab;
            _lastReceivedTabTransfer = payload.Id;
            return true;
        }
        var content = new NavigatorTabContent(state.Left.Path, null, CreateLoadingContent()) { RestoreState = state };
        var tab = new TabViewItem { Tag = content, IconSource = TabIcon(LocationCaption.Glyph(state.Left.Path)) };
        ApplyHeader(tab, LocationCaption.Title(state.Left.Path));
        AttachTab(tab, index);
        _lastReceivedTabTransfer = payload.Id;
        return true;
    }

    private void CompleteTabTransfer(TabViewItem tab, DataPackageOperation result)
    {
        // In-process moves already detach the live page. A cross-process target
        // returns Move only after owning a complete snapshot of the tab.
        if (result != DataPackageOperation.Move || _handledTabDrop || !Tabs.TabItems.Contains(tab)) return;
        // WinUI also reports Move for a reorder within the originating TabView.
        // Only a remote receiver that owns the snapshot signals our receipt.
        if (_tabTransferReceipt?.WaitOne(0) != true) return;
        DetachTab(tab);
        var page = (tab.Tag as NavigatorTabContent)?.TakeNavigatorForDisposal();
        if (tab.Tag is Views.SearchResultsPage search) search.Dispose();
        tab.Tag = null;
        if (page is not null) _ = DisposeNavigatorAsync(page);
        _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CloseIfEmpty);
    }

    private void UnwireTransferredTab(TabViewItem tab)
    {
        tab.DragStarting -= Tab_DragStarting;
        tab.DragEnter -= Tab_DragEnter;
        tab.DragOver -= FileTab_DragOver;
        tab.DragLeave -= FileTab_DragLeave;
        tab.Drop -= FileTab_Drop;
        tab.Unloaded -= FileTab_Unloaded;
        if (tab.ContextFlyout is MenuFlyout flyout) flyout.Opening -= TabFlyout_Opening;
        if (tab.Tag is NavigatorTabContent { Navigator: { } page })
        {
            page.PinnedPreviewChanged -= Navigator_PinnedPreviewChanged;
            page.PinnedPreviewVisibilityChanged -= Navigator_PinnedPreviewVisibilityChanged;
        }
    }

    private async void CloseEmptyWindowWhenIdle()
    {
        while (FileOperationLifetime.IsBusy) await FileOperationLifetime.WhenIdleAsync();
        if (!_windowClosed && Tabs.TabItems.Count == 0) Close();
    }

    private static bool IsTabDragCanceled() => (GetAsyncKeyState(0x1B) & 0x8000) != 0;

    private Point _tabDragAnchor;
    private Point _firstTabOrigin;
    private SoftwareBitmap? _tabDragBitmap;

    private async void Tab_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (sender is not TabViewItem tab || e.Cancel) return;
        if (FileOperationLifetime.IsBusy || tab.Tag is not NavigatorTabContent and not Views.SearchResultsPage
            || tab.Tag is NavigatorTabContent { Navigator.CanInstallUpdate: false })
        { e.Cancel = true; return; }
        try
        {
            var payload = CreateTabTransfer(tab);
            var json = payload.Serialize();
            if (TabTransferPayload.Parse(json) is null) { e.Cancel = true; return; }
            _tabTransferReceipt?.Dispose();
            _tabTransferReceipt = new EventWaitHandle(false, EventResetMode.ManualReset, payload.ReceiptName);
            e.Data.SetData(TabTransferPayload.Format, json);
            e.Data.RequestedOperation = DataPackageOperation.Move;
            e.AllowedOperations = DataPackageOperation.Move;
            _tabDragging = true;
            _dragOperationTab = DraggedTab = tab;
            DragSource = this;
        }
        catch (Exception error)
        {
            e.Cancel = true;
            _tabTransferReceipt?.Dispose();
            _tabTransferReceipt = null;
            App.LogFailure("StartTabTransfer", error);
            return;
        }
        var pointer = e.GetPosition(tab);
        _tabDragAnchor = new Point(Math.Clamp(pointer.X, 0, tab.ActualWidth), Math.Clamp(pointer.Y, 0, tab.ActualHeight));
        if (Tabs.TabItems.FirstOrDefault() is TabViewItem first)
            _firstTabOrigin = first.TransformToVisual(Content).TransformPoint(new Point());
        var deferral = e.GetDeferral();
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(tab);
            var pixels = await bitmap.GetPixelsAsync();
            _tabDragBitmap?.Dispose();
            _tabDragBitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels, BitmapPixelFormat.Bgra8,
                bitmap.PixelWidth, bitmap.PixelHeight, BitmapAlphaMode.Premultiplied);
            var scale = bitmap.PixelWidth / tab.ActualWidth;
            e.DragUI.SetContentFromSoftwareBitmap(_tabDragBitmap, new Point(_tabDragAnchor.X * scale, _tabDragAnchor.Y * scale));
        }
        catch (Exception error) { App.LogFailure("TabDragVisual", error); }
        finally { deferral.Complete(); }
    }

    private void Tab_DragEnter(object sender, DragEventArgs e)
    {
        // The native TabView reserves a maximum-width blank tab on DragEnter.
        // Our edge marker provides the insertion feedback without moving '+'.
        if (e.DataView.Contains(TabTransferPayload.Format)) e.Handled = true;
    }

    private void ShowTabInsertion(double x)
    {
        var origin = Tabs.TransformToVisual(AppTitleBar).TransformPoint(new Point(x, 0));
        TabInsertionMarker.Margin = new Thickness(Math.Max(0, origin.X - 1), 0, 0, 0);
        TabInsertionMarker.Visibility = Visibility.Visible;
    }

    private void HideTabInsertion() => TabInsertionMarker.Visibility = Visibility.Collapsed;

    private void PositionTornOutWindow(MainWindow window, TabViewItem tab)
    {
        if (!GetCursorPos(out var cursor)) return;
        if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } presenter)
            presenter.Restore();
        ((FrameworkElement)window.Content).UpdateLayout();
        var origin = tab.IsLoaded && tab.ActualWidth > 0
            ? tab.TransformToVisual(window.Content).TransformPoint(new Point()) : _firstTabOrigin;
        var scale = window.Content.XamlRoot?.RasterizationScale ?? Content.XamlRoot.RasterizationScale;
        var client = new NativePoint();
        ClientToScreen(window.NativeHandle, ref client);
        var frameX = client.X - window.AppWindow.Position.X;
        var frameY = client.Y - window.AppWindow.Position.Y;
        window.AppWindow.Move(new((int)Math.Round(cursor.X - (origin.X + _tabDragAnchor.X) * scale - frameX),
            (int)Math.Round(cursor.Y - (origin.Y + _tabDragAnchor.Y) * scale - frameY)));
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
}
