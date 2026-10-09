using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;

using FilesMate.Core.Entries;
using FilesMate.Core.Icons;
using FilesMate.App.Models;
using FilesMate.Platform.Windows.Icons;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.Graphics.Imaging;

namespace FilesMate.App.Icons;

internal enum ThumbnailLoadMode { Active, VisibleDeferred, Suspended }

/// <summary>
/// Binds a recycled image element through the ordered visual fallback chain:
/// picture thumbnail, bundled format art, Windows shell icon, then the caller's
/// Fluent glyph. Every asynchronous completion is guarded by the element stamp.
/// </summary>
internal static class ShellIconBinder
{
    private const int MaxDynamicImageCacheEntries = 320;
    private const long MaxDynamicImageCacheBytes = 24L * 1024 * 1024;
    private static readonly object ThumbnailCacheSync = new();
    private static readonly Services.ByteBudgetCache<ImageSource> DynamicImages = new(MaxDynamicImageCacheBytes, MaxDynamicImageCacheEntries);
    // Maps an already-published entry to its last verified file-version key.
    // Values are keys, never extra bitmap owners. Lookup must still find a
    // strong-cache entry or a genuinely live weak source for this dispatcher.
    private static readonly Services.ByteBudgetCache<string> RecentThumbnailKeys = new(256L * 1024, MaxDynamicImageCacheEntries);
    private static readonly Services.WeakValueCache<ImageSource> LiveImages = new(1024, 512L * 1024);
    private static readonly ConditionalWeakTable<DispatcherQueue, ImageCacheOwner> ImageCacheOwners = new();
    private static readonly ConditionalWeakTable<ImageSource, SourceInfo> SourceInfos = new();
    private sealed record SourceInfo(string ScopedKey, int OwnerId, long Bytes, int Generation);
    private static readonly SemaphoreSlim SpeculativeGate = new(1, 1);
    private static readonly Timer RetentionExpiry = new(static _ => UpdateRetentionExpiry(), null, Timeout.Infinite, Timeout.Infinite);
    private static long _nextRetentionOwner;
    private sealed class ImageCacheOwner(int id)
    {
        public int Id { get; } = id;
        // The weak dispatcher key owns its static assets. A retired dispatcher
        // must not leave another process-global strong source dictionary behind.
        public ConcurrentDictionary<string, ImageSource> Images { get; } = new(StringComparer.Ordinal);
    }
    private static int _nextImageCacheOwner, _dynamicSourceCreations, _bitmapSourceCreations;
    private static long _rawWarmHits;
    private static readonly ShellThumbnailService Thumbnails = new();
    private static readonly Services.VividThumbnailCache VividThumbnails = new();
    private static int _stamp;
    private static int _cacheGeneration;
    internal static int CacheGeneration => Volatile.Read(ref _cacheGeneration);
    private static readonly ConditionalWeakTable<Image, IconBinding> Bindings = new();
    private sealed record IconBinding(FontIcon Fallback, IconKey Key, string? Path, FileAttributes Attributes,
        bool Directory, FileIconKind? Kind, int Pixels, string? EntryIdentity, DispatcherQueue Dispatcher, ThumbnailLoadMode Mode);
    internal static bool UseBundledIcons { get; private set; } = true;

    internal static void SetUseBundledIcons(bool enabled)
    {
        if (UseBundledIcons == enabled) return;
        UseBundledIcons = enabled;
        // Weak keys do not keep closed tabs or recycled controls alive.
        foreach (var pair in Bindings.ToArray())
        {
            var binding = pair.Value;
            void Rebind()
            {
                if (Bindings.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, binding))
                    BindCore(pair.Key, binding.Fallback, binding.Key, binding.Path, binding.Attributes,
                        binding.Directory, binding.Kind, binding.Pixels, binding.EntryIdentity, binding.Mode);
            }
            if (binding.Dispatcher.HasThreadAccess) Rebind();
            else _ = binding.Dispatcher.TryEnqueue(Rebind);
        }
    }

    private sealed class BindingState(int stamp, bool resumedDeferred) : IDisposable
    {
        private int _disposed;

        public int Stamp { get; } = stamp;
        public bool ResumedDeferred { get; } = resumedDeferred;
        public int Generation { get; } = CacheGeneration;
        public long RawGeneration { get; } = Thumbnails.Generation;
        public ImageCacheOwner Owner { get; } = CacheOwner(DispatcherQueue.GetForCurrentThread());
        public string? ThumbnailIdentity { get; set; }
        public bool UseBundled { get; } = UseBundledIcons;
        public ThumbnailQuality Quality { get; } = App.ExplorerPreferences.ThumbnailQuality;
        public bool PreferOriginal { get; } = App.ExplorerPreferences.ShowFullThumbnails
            || App.ExplorerPreferences.ThumbnailQuality != ThumbnailQuality.Standard;

        private CancellationTokenSource? _cancellation;
        // Deferred and synchronous cached/format bindings never own async work.
        // Do not allocate a cancellation source only to dispose it in that turn.
        public CancellationTokenSource Cancellation => _cancellation ??= new();
        public bool IsCancellationRequested => _cancellation?.IsCancellationRequested == true;
        public bool IsPending => Volatile.Read(ref _disposed) == 0 && !IsCancellationRequested;

        public void Cancel()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                _cancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _cancellation?.Dispose();
            }
        }
    }

    public static IIconService Service { get; } = new WindowsSystemIconService();

    public static void ClearCache()
    {
        lock (ThumbnailCacheSync)
        {
            Interlocked.Increment(ref _cacheGeneration);
            DynamicImages.Clear();
            RecentThumbnailKeys.Clear();
            LiveImages.Clear();
            RetentionExpiry.Change(Timeout.Infinite, Timeout.Infinite);
        }
        (Service as WindowsSystemIconService)?.ClearCache();
        foreach (var pair in ImageCacheOwners) pair.Value.Images.Clear();
        Thumbnails.ClearCache();
        VividThumbnails.Clear();
        FolderPreviewBinder.ClearCache();
    }

    internal static void TraceScrollResources(long generation, long operation)
    {
        if (!FilesMate.Core.Diagnostics.PerformanceTrace.Enabled) return;
        FilesMate.Core.Diagnostics.PerformanceTrace.Record(FilesMate.Core.Diagnostics.PerformanceStage.ScrollThumbnailRequests,
            generation: generation, operation: operation, count: Thumbnails.ActiveLoadCount, auxiliary: Thumbnails.OutstandingLoadCount);
        FilesMate.Core.Diagnostics.PerformanceTrace.Record(FilesMate.Core.Diagnostics.PerformanceStage.ScrollImageCacheBytes,
            generation: generation, operation: operation, count: DynamicImages.Statistics.Bytes + Thumbnails.CacheBytes + FolderPreviewBinder.CacheBytes,
            auxiliary: DynamicImages.RetentionStatistics.Bytes);
    }

    internal static object CacheStatistics => new
    {
        DynamicEntries = DynamicImages.Statistics.Count,
        DynamicBytes = DynamicImages.Statistics.Bytes,
        DynamicHits = DynamicImages.Statistics.Hits,
        DynamicMisses = DynamicImages.Statistics.Misses,
        ThumbnailKeyEntries = RecentThumbnailKeys.Statistics.Count,
        ThumbnailKeyBytes = RecentThumbnailKeys.Statistics.Bytes,
        LiveImageEntries = LiveImages.Statistics.Count,
        LiveImageKeyBytes = LiveImages.Statistics.KeyBytes,
        LiveImageHits = LiveImages.Statistics.Hits,
        DynamicSourceCreations = Volatile.Read(ref _dynamicSourceCreations),
        TotalBitmapSourceCreations = Volatile.Read(ref _bitmapSourceCreations),
        StaticImageEntries = ImageCacheOwners.Sum(pair => pair.Value.Images.Count),
        RetainedImageEntries = DynamicImages.RetentionStatistics.Count,
        RetainedImageBytes = DynamicImages.RetentionStatistics.Bytes,
        RawThumbnailBytes = Thumbnails.CacheBytes,
        RawWarmHits = Interlocked.Read(ref _rawWarmHits),
        ThumbnailActiveLoads = Thumbnails.ActiveLoadCount,
        ThumbnailOutstandingRequests = Thumbnails.OutstandingLoadCount,
        ThumbnailLoads = Thumbnails.LoadCount,
        FolderPreviewBytes = FolderPreviewBinder.CacheBytes,
    };

    private static ImageCacheOwner CacheOwner(DispatcherQueue dispatcher) =>
        ImageCacheOwners.GetValue(dispatcher, _ => new ImageCacheOwner(Interlocked.Increment(ref _nextImageCacheOwner)));

    private static string ScopedImageKey(string key, ImageCacheOwner owner) => $"{owner.Id}|{key}";

    private static bool TryGetImage(string key, ImageCacheOwner owner, out ImageSource source)
    {
        var scoped = ScopedImageKey(key, owner);
        return owner.Images.TryGetValue(key, out source!) || DynamicImages.TryGetValue(scoped, out source)
            || LiveImages.TryGetValue(scoped, out source);
    }

    internal static Task<IconBitmap?> GetThumbnailAsync(
        string? path,
        int pixelSize,
        CancellationToken cancellationToken) => GetThumbnailAsync(path, pixelSize, cancellationToken,
            App.ExplorerPreferences.ShowFullThumbnails || App.ExplorerPreferences.ThumbnailQuality != ThumbnailQuality.Standard);

    private static async Task<IconBitmap?> GetThumbnailAsync(
        string? path,
        int pixelSize,
        CancellationToken cancellationToken,
        bool preferOriginal,
        long? generation = null)
    {
        if (path is not null && FileTypeIconCatalog.ClassifyPath(path, false) == FileIconKind.Video)
        {
            var cachedPath = await Services.ThumbnailMetadataScheduler.RunAsync(
                () => VividThumbnails.Find(path, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (cachedPath is not null)
            {
                var cached = await Thumbnails.GetAsync(cachedPath, pixelSize, cancellationToken, preferOriginal, generation).ConfigureAwait(false);
                if (cached is not null)
                    return cached;
            }
        }
        return await Thumbnails.GetAsync(path, pixelSize, cancellationToken, preferOriginal, generation).ConfigureAwait(false);
    }

    public static void Bind(
        Image image,
        FontIcon fallback,
        in FileEntryCore entry,
        string? path,
        int pixelSize,
        ThumbnailLoadMode mode = ThumbnailLoadMode.Active)
    {
        var rasterPixels = RasterizePixelSize(image, pixelSize);
        var kind = FileTypeIconCatalog.Classify(entry);
        BindCore(
            image,
            fallback,
            IconKey.From(entry, path, rasterPixels),
            path,
            entry.Attributes,
            entry.Kind == EntryKind.Directory,
            kind,
            rasterPixels,
            formatEntryIdentity: path is not null && kind is (FileIconKind.Image or FileIconKind.Video)
                ? $"{ThumbnailPathIdentity(path)}:{entry.Size}:{entry.ModifiedUtcTicks}"
                : null,
            mode: mode);
    }

    public static void BindPath(Image image, FontIcon fallback, string path, bool directory, int pixelSize)
    {
        if (FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(path, out var device))
        {
            var entry = new FileEntryCore(0, device.Name, 0, 0, 0,
                directory ? FileAttributes.Directory : FileAttributes.Normal, directory ? EntryKind.Directory : EntryKind.File);
            Bind(image, fallback, entry, path, pixelSize);
            return;
        }
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

    internal static long CreateRetentionOwner() => Interlocked.Increment(ref _nextRetentionOwner);
    internal static void ReleaseRetainedImages(long owner)
    {
        lock (ThumbnailCacheSync)
        {
            DynamicImages.ReleaseRetained(owner);
            UpdateRetentionExpiry();
        }
    }
    internal static bool HasPendingLoad(Image image) => image.Tag is BindingState { IsPending: true };

    internal static void SetLoadMode(Image image, FontIcon fallback, ThumbnailLoadMode mode,
        long retentionOwner, bool retainCompleted = false, bool settled = false)
    {
        if (!Bindings.TryGetValue(image, out var binding)) return;
        // Fast motion defers only new work. An already requested item that is
        // still visible keeps its waiter, even while metadata/native IO waits.
        if (mode == ThumbnailLoadMode.VisibleDeferred && binding.Mode == ThumbnailLoadMode.Active
            && HasPendingLoad(image)) return;
        if (mode == ThumbnailLoadMode.Suspended)
        {
            if (retainCompleted) RetainSource(image.Source, binding.Dispatcher, retentionOwner);
            if (binding.Mode == mode) return;
        }
        else if (binding.Mode == mode) return;
        BindCore(image, fallback, binding.Key, binding.Path, binding.Attributes, binding.Directory,
            binding.Kind, binding.Pixels, binding.EntryIdentity, mode, settledResume: settled);
    }

    private static void RetainSource(ImageSource? source, DispatcherQueue dispatcher, long owner)
    {
        if (source is null || owner == 0 || !SourceInfos.TryGetValue(source, out var info)
            || info.OwnerId != CacheOwner(dispatcher).Id) return;
        lock (ThumbnailCacheSync)
        {
            if (info.Generation != CacheGeneration) return;
            if (DynamicImages.Retain(info.ScopedKey, source, info.Bytes, owner,
                Services.ThumbnailViewportPolicy.RetentionMilliseconds,
                Services.ThumbnailViewportPolicy.RetainSubBudgetBytes,
                Services.ThumbnailViewportPolicy.MaxRetainedEntries))
                UpdateRetentionExpiry();
        }
    }

    private static void UpdateRetentionExpiry()
    {
        lock (ThumbnailCacheSync)
        {
            DynamicImages.PruneExpired();
            var next = DynamicImages.NextRetentionExpiry;
            RetentionExpiry.Change(next == long.MaxValue ? Timeout.Infinite : Math.Max(1, next - Environment.TickCount64), Timeout.Infinite);
        }
    }

    internal static int ThumbnailRequestPixels(int dipSize, double rasterScale)
    {
        var scale = double.IsFinite(rasterScale) && rasterScale > 0 ? rasterScale : 1;
        var rasterPixels = Math.Clamp((int)Math.Round(dipSize * scale * 2, MidpointRounding.AwayFromZero), 32, 512);
        return ThumbnailQualityPolicy.PixelSize(rasterPixels, App.ExplorerPreferences.ThumbnailQuality);
    }

    internal static async Task<bool> TryPrefetchThumbnailAsync(string path, int finalPixels, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (finalPixels <= 0 || finalPixels > 2048 || 4L * finalPixels * finalPixels > Services.ThumbnailViewportPolicy.RawBudgetBytes
            || FileTypeIconCatalog.ClassifyPath(path, false) is not (FileIconKind.Image or FileIconKind.Video)
            || !SpeculativeGate.Wait(0)) return false;
        try
        {
            // An abandoned native decoder still owns its slot. Do not place
            // another speculative request behind it or existing visible work.
            if (Thumbnails.ActiveLoadCount != 0 || Thumbnails.OutstandingLoadCount != 0) return false;
            var generation = Thumbnails.Generation;
            var preferOriginal = App.ExplorerPreferences.ShowFullThumbnails
                || App.ExplorerPreferences.ThumbnailQuality != ThumbnailQuality.Standard;
            _ = await GetThumbnailAsync(path, finalPixels, token, preferOriginal, generation).ConfigureAwait(false);
            return true;
        }
        finally { SpeculativeGate.Release(); }
    }

    private static void BindCore(
        Image image,
        FontIcon fallback,
        IconKey key,
        string? path,
        FileAttributes attributes,
        bool directory,
        FileIconKind? formatKind,
        int rasterPixels,
        string? formatEntryIdentity = null,
        ThumbnailLoadMode mode = ThumbnailLoadMode.Active,
        bool settledResume = false)
    {
        var thumbnail = formatKind is FileIconKind.Image or FileIconKind.Video;
        // Bundled SVG fallback is not a decoded thumbnail. In particular, an
        // icon-style change must be able to hide that fallback immediately.
        Bindings.TryGetValue(image, out var previous);
        var sameIdentity = previous is not null && previous.Key == key
            && previous.Path == path && previous.EntryIdentity == formatEntryIdentity;
        var resumedDeferred = settledResume && mode == ThumbnailLoadMode.Active && sameIdentity
            && previous!.Mode == ThumbnailLoadMode.VisibleDeferred;
        var keepSource = thumbnail && image.Source is WriteableBitmap
            && previous is not null && previous.Kind == formatKind
            && string.Equals(previous.Path, path,
                path?.StartsWith(FilesMate.Platform.Windows.Shell.PortableDeviceLocation.Prefix, StringComparison.Ordinal) == true
                    ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
            && previous.EntryIdentity == formatEntryIdentity;
        Bindings.Remove(image);
        Bindings.Add(image, new(fallback, key, path, attributes, directory, formatKind, rasterPixels, formatEntryIdentity,
            DispatcherQueue.GetForCurrentThread(), mode));
        if (mode == ThumbnailLoadMode.Suspended)
        {
            CancelBinding(image);
            image.Tag = null;
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            fallback.Visibility = Visibility.Visible;
            return;
        }
        var state = BeginBinding(image, resumedDeferred);
        if (thumbnail && formatEntryIdentity is not null)
        {
            var pixels = ThumbnailQualityPolicy.PixelSize(ThumbnailPixelSize(rasterPixels), state.Quality);
            state.ThumbnailIdentity = $"{formatEntryIdentity}:{pixels}:{state.PreferOriginal}";
            if (RecentThumbnailKeys.TryGetValue(state.ThumbnailIdentity, out var verifiedKey)
                && TryGetImage(verifiedKey, state.Owner, out var warmSource))
            {
                // Show a warm image in this same preparation turn. Metadata is
                // still checked below off-thread, so a changed file is replaced.
                Show(image, fallback, warmSource);
                keepSource = true;
            }
        }
        if (keepSource)
        {
            if (mode == ThumbnailLoadMode.VisibleDeferred) { state.Dispose(); return; }
            var thumbnailDispatcher = DispatcherQueue.GetForCurrentThread();
            _ = LoadAsync(image, fallback, key, path, attributes, directory, formatKind, rasterPixels, state, thumbnailDispatcher);
            return;
        }
        if (formatKind is not (FileIconKind.Image or FileIconKind.Video)
            && (!state.UseBundled || formatKind is null || FileTypeIconCatalog.PrefersShell(formatKind))
            && TryGetImage(key.CacheId, state.Owner, out var cached))
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
            && TryGetFormatAsset(knownKind, rasterPixels, state.Owner, out var formatAsset))
        {
            Show(image, fallback, formatAsset);
            if (knownKind is FileIconKind.Image or FileIconKind.Video)
            {
                if (mode == ThumbnailLoadMode.VisibleDeferred) { state.Dispose(); return; }
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
        if (mode == ThumbnailLoadMode.VisibleDeferred) { state.Dispose(); return; }
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

    private static BindingState BeginBinding(Image image, bool resumedDeferred)
    {
        CancelBinding(image);
        var state = new BindingState(Interlocked.Increment(ref _stamp), resumedDeferred);
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

    private static bool TryGetFormatAsset(FileIconKind kind, int rasterPixels, ImageCacheOwner owner, out ImageSource source)
    {
        var cacheKey = AssetCacheKey(kind, rasterPixels);
        if (TryGetImage(cacheKey, owner, out var cached))
        {
            source = cached;
            return true;
        }

        try
        {
            source = CreateFormatSvg(kind, rasterPixels);
            source = CacheStaticImage(cacheKey, source, owner);
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
                var thumbnailPixels = ThumbnailQualityPolicy.PixelSize(ThumbnailPixelSize(rasterPixels), state.Quality);
                // File metadata can block on network paths; keep it off the UI thread.
                var thumbnailKey = await ReadThumbnailCacheKeyAsync(path, thumbnailPixels, state.PreferOriginal, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (TryGetImage(thumbnailKey, state.Owner, out var thumbnailSource))
                {
                    RememberThumbnailKey(state, thumbnailKey);
                    await ShowSourceAsync(image, fallback, thumbnailSource, state, dispatcher)
                        .ConfigureAwait(false);
                    return;
                }
                // A successful raw prefetch is already decoded. Do not put a
                // warmed visible item through the cold-scroll debounce again.
                if (Thumbnails.TryGetCachedKey(thumbnailKey) is { } rawThumbnail)
                {
                    Interlocked.Increment(ref _rawWarmHits);
                    await ShowBitmapAsync(image, fallback, thumbnailKey, rawThumbnail, state, dispatcher,
                        thumbnail: true).ConfigureAwait(false);
                    return;
                }
                // Only a surface-confirmed elapsed settle skips this delay.
                // Fast-to-slow hysteresis can also resume deferred work before
                // that deadline; it keeps the original debounce, like initial
                // and standalone bindings.
                if (!state.ResumedDeferred) await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                var thumbnail = await GetThumbnailAsync(path, thumbnailPixels, cancellationToken, state.PreferOriginal, state.RawGeneration)
                    .ConfigureAwait(false);
                if (thumbnail is not null)
                {
                    await ShowBitmapAsync(
                        image,
                        fallback,
                        thumbnailKey,
                        thumbnail,
                        state,
                        dispatcher,
                        thumbnail: true).ConfigureAwait(false);
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
                var asset = await GetFormatAssetAsync(kind, rasterPixels, dispatcher, state.Owner).ConfigureAwait(false);
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
                await ShowSourceAsync(image, fallback, null, state, dispatcher).ConfigureAwait(false);
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

    private static Task<string> ReadThumbnailCacheKeyAsync(string? path, int pixels,
        bool preferOriginal, CancellationToken token) => Services.ThumbnailMetadataScheduler.RunAsync(
            () => ThumbnailCacheKey(path, pixels, preferOriginal), token);

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
        DispatcherQueue dispatcher,
        ImageCacheOwner owner)
    {
        var cacheKey = AssetCacheKey(kind, rasterPixels);
        if (TryGetImage(cacheKey, owner, out var cached))
        {
            return Task.FromResult<ImageSource?>(cached);
        }

        var completion = new TaskCompletionSource<ImageSource?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (TryGetImage(cacheKey, owner, out var existing))
                {
                    completion.TrySetResult(existing);
                    return;
                }

                completion.TrySetResult(CacheStaticImage(cacheKey, CreateFormatSvg(kind, rasterPixels), owner));
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
        DispatcherQueue dispatcher,
        bool thumbnail = false)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (state.IsCancellationRequested
                    || !ReferenceEquals(image.Tag, state))
                {
                    return;
                }

                // ClearCache is a reclamation boundary. Existing visible work
                // can finish painting, but must not refill the cleared caches.
                ImageSource source;
                lock (ThumbnailCacheSync)
                {
                    source = state.Generation == CacheGeneration
                        ? CacheDynamicImage(cacheKey, bitmap, state.Owner)
                        : ToBitmap(bitmap);
                    if (thumbnail) RememberThumbnailKey(state, cacheKey);
                }
                Show(image, fallback, source);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
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
        ImageSource? source,
        BindingState state,
        DispatcherQueue dispatcher)
    {
        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (!state.IsCancellationRequested
                    && ReferenceEquals(image.Tag, state))
                {
                    if (source is not null) Show(image, fallback, source);
                    else
                    {
                        image.Source = null;
                        image.Visibility = Visibility.Collapsed;
                        fallback.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
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
        Interlocked.Increment(ref _bitmapSourceCreations);
        var image = new WriteableBitmap(bitmap.Width, bitmap.Height);
        using var stream = image.PixelBuffer.AsStream();
        stream.Write(bitmap.Bgra, 0, bitmap.Bgra.Length);
        image.Invalidate();
        return image;
    }

    private static ImageSource CacheStaticImage(string cacheKey, ImageSource source, ImageCacheOwner owner) =>
        owner.Images.GetOrAdd(cacheKey, source);

    private static ImageSource CacheDynamicImage(string cacheKey, IconBitmap bitmap, ImageCacheOwner owner)
    {
        if (TryGetImage(cacheKey, owner, out var cached)) return cached;
        var created = ToBitmap(bitmap);
        Interlocked.Increment(ref _dynamicSourceCreations);
        var scoped = ScopedImageKey(cacheKey, owner);
        DynamicImages.Set(scoped, created, bitmap.Bgra.LongLength);
        SourceInfos.Add(created, new(scoped, owner.Id, bitmap.Bgra.LongLength, CacheGeneration));
        LiveImages.Set(scoped, created);
        return created;
    }

    private static string AssetCacheKey(FileIconKind kind, int rasterPixels) =>
        $"asset:{kind}:{rasterPixels}";

    // The device URI payload is case-sensitive Base64, not a Windows path.
    private static string ThumbnailPathIdentity(string path) =>
        path.StartsWith(FilesMate.Platform.Windows.Shell.PortableDeviceLocation.Prefix, StringComparison.Ordinal)
            ? path : path.ToLowerInvariant();

    private static void RememberThumbnailKey(BindingState state, string key)
    {
        lock (ThumbnailCacheSync)
        {
            if (state.ThumbnailIdentity is not { } identity || state.Generation != CacheGeneration
                || state.IsCancellationRequested) return;
            RecentThumbnailKeys.Set(identity, key, (identity.Length + key.Length) * 2L);
        }
    }

    private static string ThumbnailCacheKey(string? path, int pixelSize, bool preferOriginal)
    {
        return ShellThumbnailService.TryCreateKey(path, pixelSize, out var key, preferOriginal)
            ? key
            : $"thumbnail:{path ?? string.Empty}:{pixelSize}:{preferOriginal}";
    }

    private static int ThumbnailPixelSize(int rasterPixels) =>
        Math.Clamp(rasterPixels, 32, 512);
}
