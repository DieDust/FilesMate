using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Services;

public static class PortableDeviceCatalog
{
    private static readonly object Gate = new();
    private static Task<IReadOnlyList<PortableDeviceLocation>>? _read;
    public static Task<IReadOnlyList<PortableDeviceLocation>> LoadAsync()
    {
        lock (Gate)
        {
            if (_read is null || _read.IsFaulted || _read.IsCanceled) _read = ReadAsync();
            return _read;
        }
    }
    public static void Invalidate() { lock (Gate) _read = null; }
    private static async Task<IReadOnlyList<PortableDeviceLocation>> ReadAsync()
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await PortableDeviceService.GetDevicesAsync(timeout.Token).ConfigureAwait(false);
    }
}
