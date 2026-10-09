using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Icons;

internal static class FolderPreviewBinder
{
    private static readonly FolderPreviewService Service = new(ShellIconBinder.GetThumbnailAsync,
        cover: async path => (await App.FolderCustomizations.GetAsync(path).ConfigureAwait(false)).CoverPath);

    private static int _generation;

    private sealed class BindingState(string path, int pixels)
    {
        public string Path { get; } = path;
        public int Pixels { get; } = pixels;
        public CancellationTokenSource? Pending { get; set; }
        public bool Completed { get; set; }
        public int Generation { get; set; } = Volatile.Read(ref _generation);
        public ThumbnailLoadMode Mode { get; set; }
    }

    public static void Bind(Grid host, Image cover, string? folderPath, int pixelSize,
        ThumbnailLoadMode mode = ThumbnailLoadMode.Active)
    {
        Clear(host, cover);
        if (string.IsNullOrWhiteSpace(folderPath) || pixelSize <= 0
            || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(folderPath, out _)) return;
        host.Tag = new BindingState(folderPath, pixelSize);
        SetLoadMode(host, cover, mode);
    }

    internal static bool HasPendingLoad(Grid host) => host.Tag is BindingState { Pending: not null };

    internal static void SetLoadMode(Grid host, Image cover, ThumbnailLoadMode mode)
    {
        if (host.Tag is not BindingState state) return;
        state.Mode = mode;
        var generation = Volatile.Read(ref _generation);
        if (state.Generation != generation)
        {
            CancelPending(state);
            state.Generation = generation;
            state.Completed = false;
            host.Visibility = Visibility.Collapsed;
            cover.Source = null;
        }
        if (mode == ThumbnailLoadMode.Suspended)
        {
            CancelPending(state);
            state.Completed = false;
            host.Visibility = Visibility.Collapsed;
            cover.Source = null;
            return;
        }
        // A request that was already visible may finish while its viewport is
        // moving quickly. Deferred new identities never start scan/decode work.
        if (state.Pending is not null || state.Completed) return;
        if (Service.TryGetCached(state.Path, state.Pixels, out var cached))
        {
            if (cached is not null)
            {
                cover.Source = ShellIconBinder.ToBitmap(cached);
                host.Visibility = Visibility.Visible;
            }
            state.Completed = true;
            return;
        }
        if (mode != ThumbnailLoadMode.Active) return;
        var pending = new CancellationTokenSource();
        state.Pending = pending;
        _ = LoadAsync(host, cover, state, pending);
    }

    public static void Clear(Grid host, Image cover)
    {
        if (host.Tag is BindingState previous) CancelPending(previous);
        // Never defer identity revocation for a recycled presenter.
        host.Tag = null;
        host.Visibility = Visibility.Collapsed;
        cover.Source = null;
    }

    private static void CancelPending(BindingState state)
    {
        var pending = state.Pending;
        state.Pending = null;
        pending?.Cancel(); // The corresponding async frame owns disposal.
    }

    public static void ClearCache()
    {
        Interlocked.Increment(ref _generation);
        Service.ClearCache();
    }
    internal static long CacheBytes => Service.CacheBytes;

    private static async Task LoadAsync(Grid host, Image cover, BindingState state, CancellationTokenSource pending)
    {
        var token = pending.Token;
        var generation = state.Generation;
        try
        {
            var bitmap = await Service.GetAsync(state.Path, state.Pixels, token);
            if (!token.IsCancellationRequested && generation == Volatile.Read(ref _generation)
                && ReferenceEquals(host.Tag, state)
                && ReferenceEquals(state.Pending, pending))
            {
                if (bitmap is not null)
                {
                    cover.Source = ShellIconBinder.ToBitmap(bitmap);
                    host.Visibility = Visibility.Visible;
                }
                state.Completed = true;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Folder preview binding failed: {0}", error);
            if (ReferenceEquals(state.Pending, pending)) state.Completed = true;
        }
        finally
        {
            var current = ReferenceEquals(state.Pending, pending) && ReferenceEquals(host.Tag, state);
            if (ReferenceEquals(state.Pending, pending)) state.Pending = null;
            var refresh = current && !token.IsCancellationRequested && generation != Volatile.Read(ref _generation);
            pending.Dispose();
            // A cache reset can cancel the service request independently of this
            // presenter. A still-visible binding must not remain fallback forever.
            if (refresh) SetLoadMode(host, cover, state.Mode);
        }
    }
}
