using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Preview.Providers;

public sealed class DevicePreviewProvider : IPreviewProvider
{
    public bool CanHandle(string path) => PortableDeviceLocation.TryParse(path, out _);
    public async Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || !PortableDeviceLocation.TryParse(request.Path, out var device))
            return new PreviewResult.Unsupported(request.Path, "Device unavailable.");
        var entry = await PortableDeviceService.GetDetailsAsync(device, cancellationToken).ConfigureAwait(false);
        var bitmap = !entry.IsFolder ? await PortableDeviceService.GetThumbnailAsync(device, 512, cancellationToken).ConfigureAwait(false) : null;
        return new PreviewResult.Device(request.Path, entry, bitmap);
    }
}
