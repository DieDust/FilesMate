using System.Runtime.InteropServices;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Views;
using FilesMate.App.Services;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FilesMate.App.Controls.Omnibar;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private NativeFolderDropTarget? _nativeFileDrop;
    internal INativeFolderDropTarget? NativeDropTarget => _nativeFileDrop;
    private FileDetailsSurface? _nativeDropSurface;
    private Omnibar? _nativeDropOmnibar;

    private void InitializeNativeFileDrop()
    {
        if (_shellHost is null) return;
        _nativeFileDrop = new NativeFolderDropTarget(NativeHandle,
            [NativeHandle, ShellViewHandle, _shellHost.IslandHandle],
            NativeDropDestination,
            (point, destination, effect) =>
            {
                ExternalDrag.End();
                var next = NativeDropSurface(point, out var root);
                if (!ReferenceEquals(_nativeDropSurface, next)) _nativeDropSurface?.ClearExternalDragFeedback();
                _nativeDropSurface = next;
                next?.ShowNativeDropFeedback(root, destination, effect != 0);
                _nativeDropOmnibar?.NativeCrumbFeedback(null);
                _nativeDropOmnibar = TabHost.Content is NavigatorPage page ? page.NativeDropOmnibar : null;
                var crumb = effect != 0 ? _nativeDropOmnibar?.NativeCrumbAt(root) : null;
                _nativeDropOmnibar?.NativeCrumbFeedback(crumb);
                var tab = effect != 0 ? NativeDropTabAt(root) : null;
                if (tab is not null) QueueFileTabHover(tab); else CancelFileTabHover();
#if FILESMATE_UI_TEST
                TraceArchiveDrop("NativeShellFeedback", new { point.X, point.Y, destination, effect });
                if (next is null && effect != 0)
                    TraceArchiveDrop("DragResolved", new { Updated = DateTime.UtcNow, Point = root, destination,
                        Hint = destination, HintVisible = "Visible", States = Array.Empty<object>() });
#endif
            },
            () =>
            {
                _nativeDropSurface?.ClearExternalDragFeedback(); _nativeDropSurface = null;
                _nativeDropOmnibar?.NativeCrumbFeedback(null); _nativeDropOmnibar = null;
                CancelFileTabHover();
            },
            error => App.LogFailure("NativeFileDrop", error),
            (destination, _) => App.FileUndo.Push(FileUndoRecord.ShellTransfer(destination)));
    }

    private FileDetailsSurface? NativeDropSurface(DropScreenPoint screen, out Point root)
    {
        root = default;
        if (!TryNativeDropPoint(screen, out root)) return null;
        if (TabHost.Content is NavigatorPage page) return page.ExternalDragSurfaceAt(root);
        var point = root;
        return VisualTreeHelper.FindElementsInHostCoordinates(point, Content).OfType<FileDetailsSurface>()
            .FirstOrDefault(surface => surface.ContainsExternalDragPoint(point));
    }

    private string? NativeDropDestination(DropScreenPoint screen)
    {
        var surface = NativeDropSurface(screen, out var root);
        if (!TryNativeDropPoint(screen, out root)) return null;
        if (surface is not null) return surface.ExternalDropDestinationAt(root);
        if (TabHost.Content is NavigatorPage page && page.NativeDropOmnibar.NativeCrumbAt(root)?.Tag is string path) return path;
        var tab = NativeDropTabAt(root);
        var destination = tab is null ? null : FileTabDestination(tab);
        return destination is not null && Path.IsPathFullyQualified(destination) ? destination : null;
    }

    private bool TryNativeDropPoint(DropScreenPoint screen, out Point root)
    {
        root = default;
        if (_windowClosed || FileOperationLifetime.IsBusy || Content?.XamlRoot is not { } xaml
            || VisualTreeHelper.GetOpenPopupsForXamlRoot(xaml).Count != 0
            || !NativeDropScreenToClient(NativeHandle, ref screen)) return false;
        root = new Point(screen.X / xaml.RasterizationScale, screen.Y / xaml.RasterizationScale);
        return true;
    }

    private TabViewItem? NativeDropTabAt(Point root)
    {
        foreach (var tab in Tabs.TabItems.OfType<TabViewItem>())
        {
            if (!tab.IsLoaded || tab.Visibility != Visibility.Visible) continue;
            var point = tab.TransformToVisual(null).Inverse.TransformPoint(root);
            if (point.X >= 0 && point.Y >= 0 && point.X < tab.ActualWidth && point.Y < tab.ActualHeight) return tab;
        }
        return null;
    }

    [DllImport("user32.dll", EntryPoint = "ScreenToClient")]
    private static extern bool NativeDropScreenToClient(nint window, ref DropScreenPoint point);
}
