using FilesMate.Core.Icons;

namespace FilesMate.Platform.Windows.Icons;

/// <summary>
/// Shares bounded bitmap work without borrowing an individual view's lifetime.
/// </summary>
internal sealed class IconLoadCache
{
    private readonly object _sync = new();
    private readonly IconBitmapCache _cache;
    private readonly SemaphoreSlim _gate;
    private readonly Dictionary<string, PendingLoad> _pending = new(StringComparer.Ordinal);
    private long _generation;

    public IconLoadCache(long maxBytes, int concurrency)
    {
        _cache = new IconBitmapCache(maxBytes);
        _gate = new SemaphoreSlim(concurrency, concurrency);
    }

    public long CacheBytes => _cache.CurrentBytes;

    public long Generation { get { lock (_sync) return _generation; } }

    public IconBitmap? TryGetCached(string key) =>
        _cache.TryGetValue(key, out var bitmap) ? bitmap : null;

    public void Clear()
    {
        lock (_sync)
        {
            _generation++;
            _cache.Clear();
        }
        // Other windows may still be waiting for a shared load. Let it finish,
        // but do not allow work from before this clear to repopulate the cache.
    }

    public async Task<IconBitmap?> GetAsync(
        string key,
        Func<CancellationToken, Task<IconBitmap?>> load,
        CancellationToken cancellationToken,
        long? generation = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingLoad pending;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            if (!_pending.TryGetValue(key, out pending!))
            {
                pending = new PendingLoad();
                _pending.Add(key, pending);
                pending.Task = LoadAsync(key, load, generation ?? _generation, pending);
            }
            pending.Waiters++;
        }

        try
        {
            return await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            bool lastWaiter;
            lock (_sync)
            {
                lastWaiter = --pending.Waiters == 0;
                if (lastWaiter)
                {
                    // Mark this under the publication lock: cancellation is
                    // signaled outside the lock and a native load can finish
                    // in between. Such a load must never refill the cache.
                    pending.Abandoned = true;
                    if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
                        _pending.Remove(key);
                }
            }
            if (lastWaiter)
            {
                // Native extraction may already be running. Keep its gate slot
                // until it really exits, even when no caller is waiting anymore.
                if (!pending.Task.IsCompleted) pending.Cancellation.Cancel();
                _ = pending.Task.ContinueWith(completed =>
                {
                    _ = completed.Exception; // Observe failures after all views have left.
                    pending.Cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private async Task<IconBitmap?> LoadAsync(
        string key,
        Func<CancellationToken, Task<IconBitmap?>> load,
        long generation,
        PendingLoad pending)
    {
        var cancellationToken = pending.Cancellation.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = await load(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!pending.Abandoned && bitmap is not null && generation == _generation) _cache.Set(key, bitmap);
            }
            return bitmap;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class PendingLoad
    {
        public readonly CancellationTokenSource Cancellation = new();
        public Task<IconBitmap?> Task = null!;
        public int Waiters;
        public bool Abandoned;
    }
}
