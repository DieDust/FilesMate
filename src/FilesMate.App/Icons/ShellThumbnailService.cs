using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;

using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;

using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace FilesMate.App.Icons;

/// <summary>
/// Reads Windows Explorer picture thumbnails without opening the user's photo
/// application. Work is bounded and performed away from layout.
/// </summary>
internal sealed class ShellThumbnailService
{
    // UI bitmaps have their own cache; bound this duplicate raw-pixel working set.
    private const long MaxCacheBytes = 8L * 1024 * 1024;

    private readonly IconLoadCache _cache = new(MaxCacheBytes, concurrency: 2);
    private int _loads;
    internal int LoadCount => Volatile.Read(ref _loads);

    public IconBitmap? TryGetCached(string? path, int pixelSize)
    {
        if (!TryCreateKey(path, pixelSize, out var key))
        {
            return null;
        }

        return _cache.TryGetCached(key);
    }

    internal long CacheBytes => _cache.CacheBytes;

    public void ClearCache() => _cache.Clear();

    public Task<IconBitmap?> GetAsync(string? path, int pixelSize, CancellationToken cancellationToken)
    {
        var generation = _cache.Generation;
        return Task.Run(() => GetCoreAsync(path, pixelSize, generation, cancellationToken), cancellationToken);
    }

    private Task<IconBitmap?> GetCoreAsync(string? path, int pixelSize, long generation, CancellationToken cancellationToken)
    {
        if (!TryCreateKey(path, pixelSize, out var key))
        {
            return Task.FromResult<IconBitmap?>(null);
        }

        return _cache.GetAsync(key,
            token => LoadAsync(FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out _) ? path! : Path.GetFullPath(path!), pixelSize, token),
            cancellationToken, generation);
    }

    private async Task<IconBitmap?> LoadAsync(
        string path,
        int pixelSize,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device))
                return await FilesMate.Platform.Windows.Shell.PortableDeviceService.GetThumbnailAsync(device, pixelSize, cancellationToken).ConfigureAwait(false);
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.PicturesView,
                (uint)Math.Clamp(pixelSize, 32, 512),
                ThumbnailOptions.ResizeThumbnail).AsTask(cancellationToken);
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image)
            {
                return null;
            }

            var decoder = await BitmapDecoder.CreateAsync(thumbnail).AsTask(cancellationToken);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
            {
                return null;
            }

            var scale = Math.Min(
                (double)pixelSize / decoder.PixelWidth,
                (double)pixelSize / decoder.PixelHeight);
            var width = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * Math.Min(1, scale)));
            var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * Math.Min(1, scale)));
            var transform = new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
            var bitmap = new IconBitmap((int)width, (int)height, pixels.DetachPixelData());
            Interlocked.Increment(ref _loads);
            return bitmap;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static bool TryCreateKey(string? path, int pixelSize, out string key)
    {
        key = string.Empty;
        if (pixelSize > 0 && FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device)
            && FileTypeIconCatalog.IsThumbnailPath(device.Name))
        {
            key = $"device-thumbnail:{device.Uri}:{pixelSize}";
            return true;
        }
        if (pixelSize <= 0 || !FileTypeIconCatalog.IsThumbnailPath(path))
        {
            return false;
        }

        try
        {
            var canonical = Path.GetFullPath(path!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var info = new FileInfo(canonical);
            if (!info.Exists)
            {
                return false;
            }

            key = $"thumbnail:{canonical.ToLowerInvariant()}:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{pixelSize}";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
