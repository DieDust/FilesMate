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

    public Task<IconBitmap?> GetAsync(string? path, int pixelSize, CancellationToken cancellationToken, bool preferOriginal = false)
    {
        var generation = _cache.Generation;
        return Task.Run(() => GetCoreAsync(path, pixelSize, preferOriginal, generation, cancellationToken), cancellationToken);
    }

    private Task<IconBitmap?> GetCoreAsync(string? path, int pixelSize, bool preferOriginal, long generation, CancellationToken cancellationToken)
    {
        if (!TryCreateKey(path, pixelSize, out var key, preferOriginal))
        {
            return Task.FromResult<IconBitmap?>(null);
        }

        return _cache.GetAsync(key,
            token => LoadAsync(FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out _) ? path! : Path.GetFullPath(path!), pixelSize, preferOriginal, token),
            cancellationToken, generation);
    }

    private async Task<IconBitmap?> LoadAsync(
        string path,
        int pixelSize,
        bool preferOriginal,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device))
                return await FilesMate.Platform.Windows.Shell.PortableDeviceService.GetThumbnailAsync(device, pixelSize, cancellationToken).ConfigureAwait(false);
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
            // Windows' cached thumbnails can lose source detail or transparency.
            // Opt-in source decoding keeps both, while the transform bounds pixels.
            if (preferOriginal && FileTypeIconCatalog.IsImagePath(path))
            {
                var original = await TryLoadOriginalAsync(file, pixelSize, cancellationToken).ConfigureAwait(false);
                if (original is not null) return original;
            }
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.PicturesView,
                (uint)Math.Clamp(pixelSize, 32, preferOriginal ? 2048 : 512),
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

    private async Task<IconBitmap?> TryLoadOriginalAsync(StorageFile file, int pixelSize, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await file.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;
            var limit = Math.Clamp(pixelSize, 32, 2048);
            var scale = Math.Min(1, (double)limit / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            using var software = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)
                .AsTask(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new byte[checked(software.PixelWidth * software.PixelHeight * 4)];
            software.CopyToBuffer(bytes.AsBuffer());
            Interlocked.Increment(ref _loads);
            return new IconBitmap(software.PixelWidth, software.PixelHeight, bytes);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unsupported image codecs keep the existing shell-thumbnail fallback.
            return null;
        }
    }

    internal static bool TryCreateKey(string? path, int pixelSize, out string key, bool preferOriginal = false)
    {
        key = string.Empty;
        if (pixelSize > 0 && FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device)
            && FileTypeIconCatalog.IsThumbnailPath(device.Name))
        {
            key = $"device-thumbnail:{device.Uri}:{pixelSize}:{preferOriginal}";
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

            key = $"thumbnail:{canonical.ToLowerInvariant()}:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{pixelSize}:{preferOriginal}";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
