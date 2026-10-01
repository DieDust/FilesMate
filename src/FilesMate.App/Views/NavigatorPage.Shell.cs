using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Shell;
using FilesMate.Platform.Windows.Associations;
using System.Runtime.InteropServices;
using Windows.Foundation;
using FilesMate.App.Controls.FileSurface;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private readonly ShellWindowRegistration _shellWindow = new();

    internal Controls.Omnibar.Omnibar NativeDropOmnibar => Omni;

    internal FileDetailsSurface? ExternalDragSurfaceAt(Point point) =>
        _paneCount == 3 && _thirdSurface?.ContainsExternalDragPoint(point) == true ? _thirdSurface
        : _paneCount >= 2 && _rightSurface?.ContainsExternalDragPoint(point) == true ? _rightSurface
        : FileSurface.ContainsExternalDragPoint(point) ? FileSurface : null;

    private void FolderHandlerChanged(object? sender, EventArgs args) => UpdateShellWindow();

    private IReadOnlyList<string> ReadShellSelection()
    {
        // External COM clients may enter on an RPC thread. Keep all access to
        // the live surface on its dispatcher and hand back an immutable snapshot.
        if (DispatcherQueue.HasThreadAccess)
            return _disposed || !IsLoaded ? [] : SelectedPaths().ToArray();
        var reply = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (reply.Task.IsCompleted) return;
            try { reply.TrySetResult(_disposed || !IsLoaded ? [] : SelectedPaths().ToArray()); }
            catch (Exception error) { reply.TrySetException(error); }
        })) return [];
        if (reply.Task.Wait(TimeSpan.FromMilliseconds(500))) return reply.Task.GetAwaiter().GetResult();
        reply.TrySetResult([]);
        return [];
    }

    private void UpdateShellWindow()
    {
        if (_disposed || !IsLoaded) return;
        var window = App.WindowForElement(this);
        if (window is null) return;
        try
        {
            if (window.ShellViewHandle == 0 && (Environment.ProcessPath is not { } executable
                || !new DefaultFolderAssociation(new CurrentUserRegistry()).HasOurCommand(executable)))
            {
                _shellWindow.Dispose();
                return;
            }
            _shellWindow.Navigate(window.NativeHandle, ViewModel.AddressText, path =>
            {
                // Copy the native PIDL to a path before returning to the shell. Dispatch
                // selection so a COM callback never mutates an active XAML layout pass.
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (_disposed || !IsLoaded) return;
                    _pendingSelectPath = path;
                    _pendingSelectPane = ViewModel;
                    _selectAttempts = 0;
                    TryApplyPendingSelection(ViewModel);
                });
            }, window.ShellViewHandle, window.ShellViewHandle == 0 ? null : ReadShellSelection,
                (x, y) => ReadExternalDropTarget(window.NativeHandle, x, y), window.NativeDropTarget);
        }
        catch (COMException error)
        {
            App.AppendCrashRecord("ShellWindowRegistration", error);
        }
    }

    private string? ReadExternalDropTarget(nint window, int screenX, int screenY)
    {
        string? Resolve()
        {
            if (_disposed || !IsLoaded) return null;
            var point = new NativePoint { X = screenX, Y = screenY };
            if (!ScreenToClient(window, ref point)) return null;
            var scale = XamlRoot?.RasterizationScale ?? 1;
            var rootPoint = new Point(point.X / scale, point.Y / scale);
            var destination = (_paneCount == 3 ? _thirdSurface?.ExternalDropDestinationAt(rootPoint) : null)
                ?? (_paneCount >= 2 ? _rightSurface?.ExternalDropDestinationAt(rootPoint) : null)
                ?? FileSurface.ExternalDropDestinationAt(rootPoint);
#if FILESMATE_UI_TEST
            MainWindow.TraceArchiveDrop("ShellTarget", new { screenX, screenY, ClientX = point.X, ClientY = point.Y, scale, rootPoint.X, rootPoint.Y, destination });
#endif
            return destination;
        }

        if (DispatcherQueue.HasThreadAccess) return Resolve();
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (reply.Task.IsCompleted) return;
            try { reply.TrySetResult(Resolve()); }
            catch (Exception error) { reply.TrySetException(error); }
        })) return null;
        if (reply.Task.Wait(TimeSpan.FromMilliseconds(500))) return reply.Task.GetAwaiter().GetResult();
        reply.TrySetResult(null);
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
}
