using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using Microsoft.UI.Dispatching;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private readonly Dictionary<PaneViewModel, FolderStatusRequest> _folderStatusRequests = [];

    private void UpdateFolderStatus(PaneViewModel vm)
    {
        if (_disposed || !IsLoaded) return;
        if (ReferenceEquals(vm, _rightVm) && !_dualPane)
        {
            CancelFolderStatus(vm);
            return;
        }
        var path = vm.AddressText;
        var generation = vm.Navigation.CurrentGeneration;
        if (vm.IsPortableDevice)
        {
            CancelFolderStatus(vm);
            ChromeOf(vm).ZoomText = vm.IsLoading ? string.Empty : StringTable.Get(vm.CanReceiveFiles ? "Device_CanPaste" : "Device_ReadOnly");
            Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(ChromeOf(vm),
                StringTable.Get(vm.CanReceiveFiles ? "Device_WritableHint" : "Device_ReadOnlyHint"));
            return;
        }
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(ChromeOf(vm), null);
        // A status label must not silently crawl an entire drive. Folder totals
        // follow the explicit preference; drive roots always use cheap capacity data.
        if (!App.ExplorerPreferences.ShowFolderSizes || DriveCapacity.IsVolumeRoot(path))
        {
            CancelFolderStatus(vm);
            ChromeOf(vm).ZoomText = DriveCapacity.FormatFreeSpace(path) ?? string.Empty;
            return;
        }
        _folderStatusRequests.TryGetValue(vm, out var previous);
        if (previous is not null && previous.Path == path && previous.Generation == generation)
            return;

        if (previous is not null)
        {
            previous.Cancellation.Cancel();
            previous.Cancellation.Dispose();
            _folderStatusRequests.Remove(vm);
        }

        var chrome = ChromeOf(vm);
        if (HomeLocation.IsHome(path) || TagLocation.IsTag(path) || string.IsNullOrWhiteSpace(path))
        {
            chrome.ZoomText = string.Empty;
            return;
        }
        // Wait for directory navigation to complete, so rapidly passing through
        // folders does not start recursive work or display a stale total.
        chrome.ZoomText = StringTable.Get("Status_FolderCalculating");
        if (vm.IsLoading) return;
        if (!string.IsNullOrEmpty(vm.ErrorText))
        {
            chrome.ZoomText = StringTable.Get("Status_FolderUnavailable");
            return;
        }

        var request = new FolderStatusRequest(path, generation);
        _folderStatusRequests[vm] = request;
        _ = FillFolderStatusAsync(vm, request);
    }

    private async Task FillFolderStatusAsync(PaneViewModel vm, FolderStatusRequest request)
    {
        var token = request.Cancellation.Token;
        try
        {
            await Task.Delay(200, token).ConfigureAwait(false);
            var bytes = await FolderSizeCache.GetAsync(request.Path, token).ConfigureAwait(false);
            Publish(StringTable.Format("Status_FolderSize",
                DriveCapacity.FormatBytes((long)Math.Min(bytes, (ulong)long.MaxValue))));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            Publish(StringTable.Get("Status_FolderUnavailable"));
        }

        void Publish(string text) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_disposed && !token.IsCancellationRequested
                && _folderStatusRequests.TryGetValue(vm, out var current) && ReferenceEquals(current, request))
                ChromeOf(vm).ZoomText = text;
        });
    }

    private void CancelFolderStatusRequests()
    {
        foreach (var request in _folderStatusRequests.Values)
        {
            request.Cancellation.Cancel();
            request.Cancellation.Dispose();
        }
        _folderStatusRequests.Clear();
    }

    private void CancelFolderStatus(PaneViewModel vm)
    {
        if (!_folderStatusRequests.Remove(vm, out var request)) return;
        request.Cancellation.Cancel();
        request.Cancellation.Dispose();
    }

    private sealed record FolderStatusRequest(string Path, long Generation)
    {
        public CancellationTokenSource Cancellation { get; } = new();
    }
}
