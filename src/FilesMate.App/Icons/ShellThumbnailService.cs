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
    private const long MaxCacheBytes = 16L * 1024 * 1024;

    private readonly IconBitmapCache _cache = new(maxBytes: MaxCacheBytes);
    private readonly SemaphoreSlim _gate = new(2, 2);

    public IconBitmap? TryGetCached(string? path, int pixelSize)
    {
        if (!TryCreateKey(path, pixelSize, out var key))
        {
            return null;
        }

        return _cache.TryGetValue(key, out var bitmap) ? bitmap : null;
    }

    public void ClearCache() => _cache.Clear();

    public Task<IconBitmap?> GetAsync(string? path, int pixelSize, CancellationToken cancellationToken)
        => Task.Run(() => GetCoreAsync(path, pixelSize, cancellationToken), cancellationToken);

    private Task<IconBitmap?> GetCoreAsync(string? path, int pixelSize, CancellationToken cancellationToken)
    {
        if (!TryCreateKey(path, pixelSize, out var key))
        {
            return Task.FromResult<IconBitmap?>(null);
        }

        if (_cache.TryGetValue(key, out var cached))
        {
            return Task.FromResult<IconBitmap?>(cached);
        }

        return LoadAsync(key, Path.GetFullPath(path!), pixelSize, cancellationToken);
    }

    private async Task<IconBitmap?> LoadAsync(
        string key,
        string path,
        int pixelSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                cancellationToken.ThrowIfCancellationRequested();
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
                _cache.Set(key, bitmap);
                return bitmap;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static bool TryCreateKey(string? path, int pixelSize, out string key)
    {
        key = string.Empty;
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
