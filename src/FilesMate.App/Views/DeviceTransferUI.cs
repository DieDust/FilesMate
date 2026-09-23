using System.Text.Json;
using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Platform.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace FilesMate.App.Views;

/// <summary>Device clipboard payloads contain identities only, never cached file contents.</summary>
internal static class DeviceTransferUI
{
    internal const string ClipboardFormat = "FilesMate.PortableDeviceItems.v1";
    private const int MaximumPayloadBytes = 4 * 1024 * 1024;

    internal static bool HasFiles(DataPackageView view) => view.Contains(ClipboardFormat) || view.Contains(StandardDataFormats.StorageItems);

    private static byte[] EncodeItems(IEnumerable<PortableDeviceLocation> locations)
    {
        var paths = locations.Take(10001).Select(l => l.Uri).ToArray();
        if (paths.Length is 0 or > 10000 || paths.Sum(p => (long)p.Length + 3) + 2 > MaximumPayloadBytes)
            throw new IOException(StringTable.Get("Device_InvalidClipboard"));
        return JsonSerializer.SerializeToUtf8Bytes(paths);
    }

    internal static async Task SetItemsAsync(DataPackage data, IEnumerable<PortableDeviceLocation> locations)
    {
        var stream = await CreatePayloadAsync(EncodeItems(locations));
        data.RequestedOperation = DataPackageOperation.Copy;
        data.SetData(ClipboardFormat, stream);
    }

    private static async Task<InMemoryRandomAccessStream> CreatePayloadAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using var writer = new DataWriter(stream);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    internal static void SetDragItems(DataPackage data, IEnumerable<PortableDeviceLocation> locations)
    {
        var bytes = EncodeItems(locations);
        data.RequestedOperation = DataPackageOperation.Copy;
        // Publish explicit UTF-8 bytes: Windows custom clipboard formats are
        // binary streams. Use a native clonable stream, not a MemoryStream adapter.
        data.SetDataProvider(ClipboardFormat, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                request.SetData(await CreatePayloadAsync(bytes));
            }
            catch (Exception error) { App.LogFailure("DeviceClipboard", error); }
            finally { deferral.Complete(); }
        });
    }

    internal static async Task<string[]> ReadItemsAsync(DataPackageView view)
    {
        if (view.Contains(ClipboardFormat))
        {
            var payload = await view.GetDataAsync(ClipboardFormat);
            using var stream = payload switch
            {
                IRandomAccessStreamReference reference => await reference.OpenReadAsync(),
                IRandomAccessStream randomAccess => randomAccess.CloneStream(),
                _ => throw new IOException(StringTable.Get("Device_InvalidClipboard")),
            };
            if (stream.Size is 0 or > MaximumPayloadBytes) throw new IOException(StringTable.Get("Device_InvalidClipboard"));
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            var length = (uint)stream.Size;
            if (await reader.LoadAsync(length) != length) throw new IOException(StringTable.Get("Device_InvalidClipboard"));
            var bytes = new byte[length];
            reader.ReadBytes(bytes);
            var paths = JsonSerializer.Deserialize<string[]>(bytes);
            if (paths is null || paths.Length is 0 or > 10000 || paths.Any(p => !PortableDeviceLocation.TryParse(p, out _)))
                throw new IOException(StringTable.Get("Device_InvalidClipboard"));
            return paths;
        }
        if (!view.Contains(StandardDataFormats.StorageItems)) return [];
        var items = await view.GetStorageItemsAsync();
        // A virtual item with no filesystem path must not disappear silently.
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Path) || !Path.IsPathFullyQualified(item.Path)))
            throw new IOException(StringTable.Get("Device_InvalidClipboard"));
        return items.Select(item => item.Path).ToArray();
    }

    internal static async Task<DeviceCopyResult> CopyAsync(FrameworkElement host, IReadOnlyList<string> sources, string destination)
    {
        using var lifetime = FileOperationLifetime.Begin();
        // Native Shell owns the progress/cancel/conflict UI. Switching tabs does
        // not cancel a transfer, and closing the app waits for its actual return.
        var result = await PortableDeviceService.CopyAsync(sources, destination, App.WindowForElement(host)?.NativeHandle ?? 0);
        if (!result.Succeeded && host.IsLoaded && host.XamlRoot is not null)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = host.XamlRoot, Title = StringTable.Get("Command_Copy"),
                CloseButtonText = StringTable.Get("Close"),
                Content = new TextBlock { Text = Describe(result), TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            };
            ContentDialogTheme.Apply(dialog, host);
            await dialog.ShowAsync();
        }
        return result;
    }

    internal static string Describe(DeviceCopyResult result) => result.Succeeded
        ? StringTable.Format("Device_CopyCompleted", result.Completed)
        : StringTable.Format(result.Cancelled ? "Device_CopyCancelled" : "Device_CopyIncomplete", result.Completed, result.Skipped);
}
