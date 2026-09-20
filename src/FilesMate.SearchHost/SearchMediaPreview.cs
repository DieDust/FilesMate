using FilesMate.App.Icons;
using FilesMate.App.Services;
using FilesMate.Core.Icons;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FilesMate.SearchHost;

internal sealed class SearchMediaPreview
{
    private readonly ShellThumbnailService _thumbnails = new();
    private readonly VividThumbnailCache _vivid = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, BitmapSource> _images = new(StringComparer.Ordinal);
    private readonly Queue<(string Key, long Bytes)> _order = new();
    private long _bytes;
    private int _generation;
    internal void Clear()
    {
        lock (_sync) { _images.Clear(); _order.Clear(); _bytes = 0; _generation++; }
        _thumbnails.ClearCache();
    }

    internal async Task<BitmapSource?> LoadAsync(string path, int size, CancellationToken token)
    {
        var generation = Volatile.Read(ref _generation);
        var key = await Task.Run(() => ShellThumbnailService.TryCreateKey(path, size, out var value) ? value : null, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (key is null) return null;
        lock (_sync) { if (_images.TryGetValue(key, out var cached)) return cached; }
        // Reuse the folder's optional cache -> Windows thumbnail fallback.
        var bitmap = await Task.Run(async () =>
        {
            if (FileTypeIconCatalog.ClassifyPath(path, false) == FileIconKind.Video)
            {
                var cached = _vivid.Find(path, token);
                if (cached is not null && await _thumbnails.GetAsync(cached, size, token) is { } frame) return frame;
            }
            return await _thumbnails.GetAsync(path, size, token);
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (bitmap is null) return null;
        var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null, bitmap.Bgra, bitmap.Width * 4);
        image.Freeze();
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (generation != _generation) return image;
            if (_images.TryGetValue(key, out var existing)) return existing;
            _images.Add(key, image);
            var bytes = bitmap.Bgra.LongLength;
            _bytes += bytes;
            _order.Enqueue((key, bytes));
            while ((_bytes > 16L * 1024 * 1024 || _images.Count > 128) && _order.TryDequeue(out var oldest))
            {
                _images.Remove(oldest.Key);
                _bytes -= oldest.Bytes;
            }
        }
        return image;
    }
}
