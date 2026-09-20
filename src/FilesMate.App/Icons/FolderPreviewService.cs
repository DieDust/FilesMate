using System.IO;
using FilesMate.Core.Icons;

namespace FilesMate.App.Icons;

internal sealed class FolderPreviewService
{
    internal const int MaxScannedEntries = 256;
    private const int MaxCandidates = 4;
    private const int MaxCacheEntries = 1024;
    private const long MaxCacheBytes = 8L * 1024 * 1024;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(2, 2);
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Request> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, int, CancellationToken, Task<IconBitmap?>> _thumbnail;
    private readonly Func<string, CancellationToken, IReadOnlyList<string>> _scan;
    private readonly TimeProvider _time;
    private readonly Func<string, Task<string?>>? _cover;
    private long _cacheBytes, _access;
    internal long CacheBytes { get { lock (_sync) return _cacheBytes; } }
    private int _epoch;

    public FolderPreviewService(
        Func<string, int, CancellationToken, Task<IconBitmap?>> thumbnail,
        Func<string, CancellationToken, IReadOnlyList<string>>? scan = null,
        TimeProvider? time = null,
        Func<string, Task<string?>>? cover = null)
    {
        _thumbnail = thumbnail;
        _scan = scan ?? EnumerateCandidates;
        _time = time ?? TimeProvider.System;
        _cover = cover;
    }

    public async Task<IconBitmap?> GetAsync(string folderPath, int pixelSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Normalization is lexical. All filesystem access happens inside Task.Run.
        var path = Path.GetFullPath(folderPath);
        var key = path + ":" + pixelSize;
        Request request;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (_time.GetUtcNow() < cached.Expires)
                {
                    cached.LastAccess = ++_access;
                    return cached.Bitmap;
                }
                _cache.Remove(key);
                _cacheBytes -= cached.Bytes;
            }

            if (!_inflight.TryGetValue(key, out request!))
            {
                request = new Request();
                var epoch = _epoch;
                var token = request.Cancellation.Token;
                request.Task = Task.Run(() => LoadAsync(key, path, pixelSize, epoch, token), token);
                _inflight.Add(key, request);
            }
            request.Waiters++;
        }

        try
        {
            return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                if (--request.Waiters == 0)
                {
                    if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                        _inflight.Remove(key);
                    request.Cancellation.Cancel();
                    _ = request.Task.ContinueWith(task =>
                    {
                        _ = task.Exception;
                        request.Cancellation.Dispose();
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
    }

    public bool TryGetCached(string folderPath, int pixelSize, out IconBitmap? bitmap)
    {
        var key = Path.GetFullPath(folderPath) + ":" + pixelSize;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached) && _time.GetUtcNow() < cached.Expires)
            {
                cached.LastAccess = ++_access;
                bitmap = cached.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    public void ClearCache()
    {
        lock (_sync)
        {
            _epoch++;
            _cache.Clear();
            _cacheBytes = 0;
            foreach (var request in _inflight.Values)
                request.Cancellation.Cancel();
            _inflight.Clear();
        }
    }

    private async Task<IconBitmap?> LoadAsync(string key, string path, int size, int epoch, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            IconBitmap? bitmap = null;
            if (_cover is not null)
            {
                try
                {
                    var chosen = await _cover(path).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (chosen is not null && File.Exists(chosen))
                        bitmap = await _thumbnail(chosen, size, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A moved or undecodable custom cover falls back to automatic selection.
                }
            }
            try
            {
                foreach (var candidate in bitmap is null ? _scan(path, token) : Array.Empty<string>())
                {
                    token.ThrowIfCancellationRequested();
                    bitmap = await _thumbnail(candidate, size, token).ConfigureAwait(false);
                    if (bitmap is not null)
                        break;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Unreadable folders keep their icon and a short negative cache.
            }

            token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var bytes = bitmap?.Bgra.LongLength ?? 0;
                if (_epoch == epoch && !token.IsCancellationRequested && bytes <= MaxCacheBytes)
                {
                    if (_cache.Remove(key, out var old))
                        _cacheBytes -= old.Bytes;
                    while (_cache.Count > 0 && (_cache.Count >= MaxCacheEntries || _cacheBytes + bytes > MaxCacheBytes))
                    {
                        var oldest = _cache.MinBy(pair => pair.Value.LastAccess);
                        _cache.Remove(oldest.Key);
                        _cacheBytes -= oldest.Value.Bytes;
                    }
                    _cache[key] = new(bitmap, _time.GetUtcNow().AddSeconds(bitmap is null ? 15 : 120), bytes) { LastAccess = ++_access };
                    _cacheBytes += bytes;
                }
            }
            return bitmap;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static IReadOnlyList<string> EnumerateCandidates(string folderPath, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        var images = new List<string>();
        var videos = new List<string>();
        var scanned = 0;
        foreach (var entry in new DirectoryInfo(folderPath).EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.Directory) == 0)
            {
                if (FileTypeIconCatalog.IsImagePath(entry.Name))
                    images.Add(entry.FullName);
                else if (videos.Count < MaxCandidates && FileTypeIconCatalog.IsThumbnailPath(entry.Name))
                    videos.Add(entry.FullName);
            }
            if (++scanned >= MaxScannedEntries || images.Count >= MaxCandidates)
                break;
        }
        // Still images are cheaper to decode and usually make better covers.
        return images.Concat(videos).Take(MaxCandidates).ToArray();
    }

    private sealed class Request
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Task<IconBitmap?> Task { get; set; } = null!;
        public int Waiters { get; set; }
    }

    private sealed record CacheEntry(IconBitmap? Bitmap, DateTimeOffset Expires, long Bytes)
    {
        public long LastAccess { get; set; }
    }
}
