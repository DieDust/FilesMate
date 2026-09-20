using System.IO;

using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace FilesMate.App.Sharing;

public sealed class WindowsShareService : IShareService, IDisposable
{
    private readonly nint _hwnd;
    private readonly DataTransferManager _manager;
    private readonly TypedEventHandler<DataTransferManager, DataRequestedEventArgs> _handler;
    private IReadOnlyList<IStorageItem> _items = [];
    private string _title = "FilesMate";
    private string _fallback = string.Empty;
    private bool _disposed;

    public WindowsShareService(nint hwnd)
    {
        if (hwnd == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd));
        }

        _hwnd = hwnd;
        _manager = DataTransferManagerInterop.GetForWindow(hwnd)
            ?? throw new InvalidOperationException("Windows sharing is unavailable for this window.");
        _handler = OnDataRequested;
        _manager.DataRequested += _handler;
    }

    public async Task<ShareResult> ShareAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
        {
            return new ShareResult(false, false, null);
        }

        var fallbackText = string.Join(Environment.NewLine, normalized);
        var items = new List<IStorageItem>(normalized.Length);
        foreach (var path in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                items.Add(Directory.Exists(path)
                    ? await StorageFolder.GetFolderFromPathAsync(path)
                    : await StorageFile.GetFileFromPathAsync(path));
            }
            catch (Exception)
            {
            }
        }

        _items = items;
        _fallback = fallbackText;
        _title = items.Count == 1
            ? items[0].Name
            : items.Count > 1
                ? items[0].Name + " +" + (items.Count - 1)
                : Path.GetFileName(normalized[0].TrimEnd('\\', '/'));
        DataTransferManagerInterop.ShowShareUIForWindow(_hwnd);
        return new ShareResult(true, items.Count == 0, items.Count == 0 ? fallbackText : null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.DataRequested -= _handler;
    }

    private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var request = args.Request;
        var deferral = request.GetDeferral();
        try
        {
            var title = string.IsNullOrWhiteSpace(_title) ? "FilesMate" : _title;
            request.Data.Properties.Title = title;
            request.Data.Properties.ApplicationName = "FilesMate";
            request.Data.RequestedOperation = DataPackageOperation.Copy;
            if (_items.Count > 0)
            {
                request.Data.SetStorageItems(_items);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_fallback))
            {
                request.Data.SetText(_fallback);
                return;
            }

            request.FailWithDisplayText("Nothing to share.");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
