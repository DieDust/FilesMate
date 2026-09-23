using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App.Icons;

internal static class FolderPreviewBinder
{
    private static readonly FolderPreviewService Service = new(ShellIconBinder.GetThumbnailAsync,
        cover: async path => (await App.FolderCustomizations.GetAsync(path).ConfigureAwait(false)).CoverPath);

    private sealed class BindingState
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public bool Finished { get; set; }
    }

    public static void Bind(Grid host, Image cover, string? folderPath, int pixelSize)
    {
        Clear(host, cover);
        if (string.IsNullOrWhiteSpace(folderPath) || pixelSize <= 0
            || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(folderPath, out _))
            return;

        var state = new BindingState();
        host.Tag = state;
        _ = LoadAsync(host, cover, folderPath, pixelSize, state);
    }

    public static void Clear(Grid host, Image cover)
    {
        if (host.Tag is BindingState { Finished: false } previous)
            previous.Cancellation.Cancel();
        host.Tag = null;
        host.Visibility = Visibility.Collapsed;
        cover.Source = null;
    }

    public static void ClearCache() => Service.ClearCache();
    internal static long CacheBytes => Service.CacheBytes;

    private static async Task LoadAsync(Grid host, Image cover, string path, int size, BindingState state)
    {
        var token = state.Cancellation.Token;
        try
        {
            if (Service.TryGetCached(path, size, out var cached))
            {
                if (cached is not null && ReferenceEquals(host.Tag, state))
                {
                    cover.Source = ShellIconBinder.ToBitmap(cached);
                    host.Visibility = Visibility.Visible;
                }
                return;
            }

            // Recycle fast-scrolling tiles before starting IO; retain the UI context for painting.
            await Task.Delay(100, token);
            var bitmap = await Service.GetAsync(path, size, token);
            if (!token.IsCancellationRequested && ReferenceEquals(host.Tag, state) && bitmap is not null)
            {
                cover.Source = ShellIconBinder.ToBitmap(bitmap);
                host.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Folder preview binding failed: {0}", error);
        }
        finally
        {
            state.Finished = true;
            state.Cancellation.Dispose();
        }
    }
}
