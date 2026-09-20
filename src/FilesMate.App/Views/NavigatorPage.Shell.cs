using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Shell;
using FilesMate.Platform.Windows.Associations;
using System.Runtime.InteropServices;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private readonly ShellWindowRegistration _shellWindow = new();

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
            }, window.ShellViewHandle, window.ShellViewHandle == 0 ? null : ReadShellSelection);
        }
        catch (COMException error)
        {
            App.AppendCrashRecord("ShellWindowRegistration", error);
        }
    }
}
