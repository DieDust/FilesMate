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
    // Shared per cache instance, not per row or pane. This accommodates a wide
    // visible viewport without allowing an unbounded native-waiter backlog.
    private readonly int _maxOutstanding;
    // One shared admission signal, rather than one queued native-load/CTS per
    // caller. View lifetimes cancel their admission wait without losing another
    // view's shared request or dropping a still-visible image.
    private TaskCompletionSource? _capacityChanged;
    private readonly Dictionary<(string Key, long Generation), PendingLoad> _pending = new();
    private long _generation;
    private int _activeLoads;

    public IconLoadCache(long maxBytes, int concurrency, int maxOutstanding = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOutstanding, concurrency);
        _cache = new IconBitmapCache(maxBytes);
        _gate = new SemaphoreSlim(concurrency, concurrency);
        _maxOutstanding = maxOutstanding;
    }

    public long CacheBytes => _cache.CurrentBytes;
    internal int ActiveLoads => Volatile.Read(ref _activeLoads);
    internal int OutstandingRequests { get { lock (_sync) return _pending.Count; } }

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
        (string Key, long Generation) requestKey;
        lock (_sync) requestKey = (key, generation ?? _generation);
        while (true)
        {
            Task capacity;
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_cache.TryGetValue(key, out var cached)) return cached;
                if (_pending.TryGetValue(requestKey, out pending!))
                {
                    // Coalescing remains available even when admission is full.
                    pending.Waiters++;
                    break;
                }
                if (_pending.Count < _maxOutstanding)
                {
                    pending = new PendingLoad { Waiters = 1 };
                    _pending.Add(requestKey, pending);
                    pending.Task = LoadAsync(key, load, requestKey.Generation, pending);
                    break;
                }
                _capacityChanged ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                capacity = _capacityChanged.Task;
            }
            // Keep the captured generation across admission waits: a cache
            // clear must not turn pre-clear work into a fresh cache publisher.
            await capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    if (_pending.TryGetValue(requestKey, out var current) && ReferenceEquals(current, pending))
                    {
                        _pending.Remove(requestKey);
                        var capacity = _capacityChanged;
                        _capacityChanged = null;
                        capacity?.TrySetResult();
                    }
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
        Interlocked.Increment(ref _activeLoads);
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
            Interlocked.Decrement(ref _activeLoads);
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
