using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;

using FilesMate.Core.Entries;
using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics.Imaging;

namespace FilesMate.App.Icons;

/// <summary>
/// Binds a recycled image element through the ordered visual fallback chain:
/// picture thumbnail, bundled format art, Windows shell icon, then the caller's
/// Fluent glyph. Every asynchronous completion is guarded by the element stamp.
/// </summary>
internal static class ShellIconBinder
{
    private const int MaxDynamicImageCacheEntries = 320;
    private const long MaxDynamicImageCacheBytes = 24L * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, ImageSource> Images = new(StringComparer.Ordinal);
    private static readonly Services.ByteBudgetCache<ImageSource> DynamicImages = new(MaxDynamicImageCacheBytes, MaxDynamicImageCacheEntries);
    private static readonly ShellThumbnailService Thumbnails = new();
    private static readonly Services.VividThumbnailCache VividThumbnails = new();
    private static int _stamp;
    private static readonly ConditionalWeakTable<Image, IconBinding> Bindings = new();
    private sealed record IconBinding(FontIcon Fallback, IconKey Key, string? Path, FileAttributes Attributes,
        bool Directory, FileIconKind? Kind, int Pixels);
    internal static bool UseBundledIcons { get; private set; } = true;

    internal static void SetUseBundledIcons(bool enabled)
    {
        if (UseBundledIcons == enabled) return;
        UseBundledIcons = enabled;
        // Weak keys do not keep closed tabs or recycled controls alive.
        foreach (var pair in Bindings.ToArray())
        {
            var binding = pair.Value;
            BindCore(pair.Key, binding.Fallback, binding.Key, binding.Path, binding.Attributes,
                binding.Directory, binding.Kind, binding.Pixels);
        }
    }

    private sealed class BindingState(int stamp) : IDisposable
    {
        private int _disposed;

        public int Stamp { get; } = stamp;
        public bool UseBundled { get; } = UseBundledIcons;

        public CancellationTokenSource Cancellation { get; } = new();

        public void Cancel()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Cancellation.Dispose();
            }
        }
    }

    public static IIconService Service { get; } = new WindowsSystemIconService();

    public static void ClearCache()
    {
        (Service as WindowsSystemIconService)?.ClearCache();
        Images.Clear();
        DynamicImages.Clear();
        Thumbnails.ClearCache();
        VividThumbnails.Clear();
        FolderPreviewBinder.ClearCache();
    }

    internal static object CacheStatistics => new
    {
        DynamicEntries = DynamicImages.Statistics.Count,
        DynamicBytes = DynamicImages.Statistics.Bytes,
        DynamicHits = DynamicImages.Statistics.Hits,
        DynamicMisses = DynamicImages.Statistics.Misses,
        RawThumbnailBytes = Thumbnails.CacheBytes,
        ThumbnailLoads = Thumbnails.LoadCount,
        FolderPreviewBytes = FolderPreviewBinder.CacheBytes,
    };

    private static bool TryGetImage(string key, out ImageSource source) =>
        Images.TryGetValue(key, out source!) || DynamicImages.TryGetValue(key, out source);

    internal static Task<IconBitmap?> GetThumbnailAsync(
        string? path,
        int pixelSize,
        CancellationToken cancellationToken) => Task.Run(async () =>
        {
            if (path is not null && FileTypeIconCatalog.ClassifyPath(path, false) == FileIconKind.Video)
            {
                var cachedPath = VividThumbnails.Find(path, cancellationToken);
                if (cachedPath is not null)
                {
                    var cached = await Thumbnails.GetAsync(cachedPath, pixelSize, cancellationToken).ConfigureAwait(false);
                    if (cached is not null)
                        return cached;
                }
            }
            return await Thumbnails.GetAsync(path, pixelSize, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public static void Bind(
        Image image,
        FontIcon fallback,
        in FileEntryCore entry,
        string? path,
        int pixelSize)
    {
        var rasterPixels = RasterizePixelSize(image, pixelSize);
        BindCore(
            image,
            fallback,
            IconKey.From(entry, path, rasterPixels),
            path,
            entry.Attributes,
            entry.Kind == EntryKind.Directory,
            FileTypeIconCatalog.Classify(entry),
            rasterPixels);
    }

    public static void BindPath(Image image, FontIcon fallback, string path, bool directory, int pixelSize)
    {
        var rasterPixels = RasterizePixelSize(image, pixelSize);
        BindCore(
            image,
            fallback,
            IconKey.ForPath(path, directory, rasterPixels),
            path,
            directory ? FileAttributes.Directory : FileAttributes.Normal,
            directory,
            FileTypeIconCatalog.ClassifyPath(path, directory),
            rasterPixels);
    }

    public static void Clear(Image image, FontIcon fallback)
    {
        Bindings.Remove(image);
        CancelBinding(image);
        image.Tag = null;
        image.Source = null;
        image.Visibility = Visibility.Collapsed;
        fallback.Visibility = Visibility.Visible;
    }

    private static void BindCore(
        Image image,
        FontIcon fallback,
        IconKey key,
        string? path,
        FileAttributes attributes,
        bool directory,
        FileIconKind? formatKind,
        int rasterPixels)
    {
        Bindings.Remove(image);
        Bindings.Add(image, new(fallback, key, path, attributes, directory, formatKind, rasterPixels));
        var state = BeginBinding(image);
        if (formatKind is not (FileIconKind.Image or FileIconKind.Video)
            && (!state.UseBundled || formatKind is null || FileTypeIconCatalog.PrefersShell(formatKind))
            && TryGetImage(key.CacheId, out var cached))
        {
            Show(image, fallback, cached);
            state.Dispose();
            return;
        }

        // Known format art is project-owned and can be created immediately on
        // the UI thread. Showing it synchronously prevents a recycled tile from
        // flashing a mismatched Fluent glyph while the async shell/thumbnail
        // path is still warming up.
        if (state.UseBundled && formatKind is FileIconKind knownKind
            && !FileTypeIconCatalog.PrefersShell(formatKind)
            && TryGetFormatAsset(knownKind, rasterPixels, out var formatAsset))
        {
            Show(image, fallback, formatAsset);
            if (knownKind is FileIconKind.Image or FileIconKind.Video)
            {
                var thumbnailDispatcher = DispatcherQueue.GetForCurrentThread();
                _ = LoadAsync(image, fallback, key, path, attributes, directory, formatKind, rasterPixels, state, thumbnailDispatcher);
            }
            else
            {
                state.Dispose();
            }

            return;
        }

        fallback.Visibility = Visibility.Visible;
        image.Visibility = Visibility.Collapsed;
        image.Source = null;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _ = LoadAsync(image, fallback, key, path, attributes, directory, formatKind, rasterPixels, state, dispatcher);
    }

    /// <summary>
    /// WinUI <see cref="SvgImageSource"/> is a bitmap decode. Rasterize at 2×
    /// physical pixels so badge strokes and folder edges downsample onto the
    /// display grid instead of being painted 1:1 with coarse Direct2D AA.
    /// </summary>
    internal static int RasterizePixelSize(XamlRoot? root, int dipSize)
    {
        var scale = root?.RasterizationScale ?? 1;
        return Math.Clamp(
            (int)Math.Round(dipSize * scale * 2, MidpointRounding.AwayFromZero),
            32,
            512);
    }

    internal static int RasterizePixelSize(UIElement element, int dipSize) =>
        RasterizePixelSize(element.XamlRoot, dipSize);

    private static BindingState BeginBinding(Image image)
    {
        CancelBinding(image);
        var state = new BindingState(Interlocked.Increment(ref _stamp));
        image.Tag = state;
        return state;
    }

    private static void CancelBinding(Image image)
    {
        if (image.Tag is BindingState previous)
        {
            previous.Cancel();
        }
    }

    private static bool TryGetFormatAsset(FileIconKind kind, int rasterPixels, out ImageSource source)
    {
        var cacheKey = AssetCacheKey(kind, rasterPixels);
        if (TryGetImage(cacheKey, out var cached))
        {
            source = cached;
            return true;
        }

        try
        {
            source = CreateFormatSvg(kind, rasterPixels);
            source = CacheStaticImage(cacheKey, source);
            return true;
        }
        catch (Exception)
        {
            source = null!;
            return false;
        }
    }

    private static async Task LoadAsync(
        Image image,
        FontIcon fallback,
        IconKey key,
        string? path,
        FileAttributes attributes,
        bool directory,
        FileIconKind? formatKind,
        int rasterPixels,
        BindingState state,
        DispatcherQueue dispatcher)
    {
        try
        {
            var cancellationToken = state.Cancellation.Token;
            if (formatKind is FileIconKind.Image or FileIconKind.Video)
            {
                var thumbnailPixels = ThumbnailPixelSize(rasterPixels);
                // File metadata can block on network paths; keep it off the UI thread.
                var thumbnailKey = await Task.Run(() => ThumbnailCacheKey(path, thumbnailPixels), cancellationToken)
                    .ConfigureAwait(false);
                if (TryGetImage(thumbnailKey, out var thumbnailSource))
                {
                    await ShowSourceAsync(image, fallback, thumbnailSource, state, dispatcher)
                        .ConfigureAwait(false);
                    return;
                }
                // Recycled off-screen tiles cancel before decoding during rapid scrolling.
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                var thumbnail = await GetThumbnailAsync(path, thumbnailPixels, cancellationToken)
                    .ConfigureAwait(false);
                if (thumbnail is not null)
                {
                    await ShowBitmapAsync(
                        image,
                        fallback,
                        thumbnailKey,
                        thumbnail,
                        state,
                        dispatcher).ConfigureAwait(false);
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (FileTypeIconCatalog.PrefersShell(formatKind))
            {
                var shellPreferred = await GetShellBitmapAsync(key, path, attributes, directory, cancellationToken)
                    .ConfigureAwait(false);
                if (shellPreferred is not null)
                {
                    await ShowBitmapAsync(image, fallback, key.CacheId, shellPreferred, state, dispatcher)
                        .ConfigureAwait(false);
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (state.UseBundled && formatKind is FileIconKind kind)
            {
                var asset = await GetFormatAssetAsync(kind, rasterPixels, dispatcher).ConfigureAwait(false);
                if (asset is not null)
                {
                    await ShowSourceAsync(
                        image,
                        fallback,
                        asset,
                        state,
                        dispatcher).ConfigureAwait(false);
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = await GetShellBitmapAsync(key, path, attributes, directory, cancellationToken)
                .ConfigureAwait(false);

            if (bitmap is null)
            {
                return;
            }

            await ShowBitmapAsync(image, fallback, key.CacheId, bitmap, state, dispatcher)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Icon binding failed: {0}", error);
        }
        finally
        {
            state.Dispose();
        }
    }

    private static async Task<IconBitmap?> GetShellBitmapAsync(
        IconKey key,
        string? path,
        FileAttributes attributes,
        bool directory,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Service.GetAsync(key, path, attributes, directory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SvgImageSource CreateFormatSvg(FileIconKind kind, int rasterPixels) =>
        new(new Uri(FileTypeIconCatalog.AssetUri(kind)))
        {
            RasterizePixelWidth = rasterPixels,
            RasterizePixelHeight = rasterPixels,
        };

    private static Task<ImageSource?> GetFormatAssetAsync(
        FileIconKind kind,
        int rasterPixels,
        DispatcherQueue dispatcher)
    {
        var cacheKey = AssetCacheKey(kind, rasterPixels);
        if (TryGetImage(cacheKey, out var cached))
        {
            return Task.FromResult<ImageSource?>(cached);
        }

        var completion = new TaskCompletionSource<ImageSource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (TryGetImage(cacheKey, out var existing))
                {
                    completion.TrySetResult(existing);
                    return;
                }

                completion.TrySetResult(CacheStaticImage(cacheKey, CreateFormatSvg(kind, rasterPixels)));
            }
            catch (Exception)
            {
                completion.TrySetResult(null);
            }
        }))
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    private static Task ShowBitmapAsync(
        Image image,
        FontIcon fallback,
        string cacheKey,
        IconBitmap bitmap,
        BindingState state,
        DispatcherQueue dispatcher)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (state.Cancellation.IsCancellationRequested
                    || !ReferenceEquals(image.Tag, state))
                {
                    return;
                }

                var source = CacheDynamicImage(cacheKey, bitmap);
                Show(image, fallback, source);
            }
            finally
            {
                completion.TrySetResult(null);
            }
        }))
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    private static Task ShowSourceAsync(
        Image image,
        FontIcon fallback,
        ImageSource source,
        BindingState state,
        DispatcherQueue dispatcher)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (!state.Cancellation.IsCancellationRequested
                    && ReferenceEquals(image.Tag, state))
                {
                    Show(image, fallback, source);
                }
            }
            finally
            {
                completion.TrySetResult(null);
            }
        }))
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
    }

    private static void Show(Image image, FontIcon fallback, ImageSource source)
    {
        image.Source = source;
        image.Visibility = Visibility.Visible;
        fallback.Visibility = Visibility.Collapsed;
    }

    internal static SoftwareBitmap ToSoftwareBitmap(IconBitmap bitmap)
    {
        var software = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            bitmap.Width,
            bitmap.Height,
            BitmapAlphaMode.Premultiplied);
        software.CopyFromBuffer(bitmap.Bgra.AsBuffer());
        return software;
    }

    internal static WriteableBitmap ToBitmap(IconBitmap bitmap)
    {
        var image = new WriteableBitmap(bitmap.Width, bitmap.Height);
        using var stream = image.PixelBuffer.AsStream();
        stream.Write(bitmap.Bgra, 0, bitmap.Bgra.Length);
        image.Invalidate();
        return image;
    }

    private static ImageSource CacheStaticImage(string cacheKey, ImageSource source) =>
        Images.GetOrAdd(cacheKey, source);

    private static ImageSource CacheDynamicImage(string cacheKey, IconBitmap bitmap)
    {
        if (DynamicImages.TryGetValue(cacheKey, out var cached)) return cached;
        var created = ToBitmap(bitmap);
        DynamicImages.Set(cacheKey, created, bitmap.Bgra.LongLength);
        return created;
    }

    private static string AssetCacheKey(FileIconKind kind, int rasterPixels) =>
        $"asset:{kind}:{rasterPixels}";

    private static string ThumbnailCacheKey(string? path, int pixelSize)
    {
        return ShellThumbnailService.TryCreateKey(path, pixelSize, out var key)
            ? key
            : $"thumbnail:{path ?? string.Empty}:{pixelSize}";
    }

    private static int ThumbnailPixelSize(int rasterPixels) =>
        Math.Clamp(rasterPixels, 32, 512);
}
