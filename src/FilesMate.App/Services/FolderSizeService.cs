namespace FilesMate.App.Services;

/// <summary>
/// Coalesces requests without giving the first caller ownership of the shared walk.
/// Queued requests await asynchronously; only two filesystem walks occupy workers.
/// </summary>
public sealed class FolderSizeService
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(2, 2);
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Request> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Action<ulong>?, ulong> _measure;
    private readonly TimeProvider _time;
    private readonly int _capacity;
    private long _access;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public FolderSizeService(Func<string, CancellationToken, Action<ulong>?, ulong> measure,
        TimeProvider? time = null, int capacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _measure = measure;
        _time = time ?? TimeProvider.System;
        _capacity = capacity;
    }

    public event Action? SizeCached;

    public bool TryGet(string path, out ulong size)
    {
        size = 0;
        if (string.IsNullOrWhiteSpace(path)) return false;
        lock (_sync) return TryGetCore(Normalize(path), out size);
    }

    private bool TryGetCore(string key, out ulong size)
    {
        size = 0;
        if (!_cache.TryGetValue(key, out var entry)) return false;
        if (_time.GetUtcNow() >= entry.Expires)
        {
            _cache.Remove(key);
            return false;
        }
        _cache[key] = entry with { Access = ++_access };
        size = entry.Size;
        return true;
    }

    public async Task<ulong> GetAsync(string path, CancellationToken token = default, Action<ulong>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        var key = Normalize(path);
        Request request;
        var subscriber = new Subscriber(progress);
        lock (_sync)
        {
            if (TryGetCore(key, out var cached)) return cached;
            if (!_inflight.TryGetValue(key, out request!))
            {
                request = new Request();
                _inflight.Add(key, request);
                request.Task = RunAsync(key, request);
                // Observe the worker even when all callers have already canceled.
                _ = request.Task.ContinueWith(task =>
                {
                    _ = task.Exception;
                    request.Cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
            request.Subscribers.Add(subscriber);
        }
        try { return await request.Task.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            lock (_sync)
            {
                request.Subscribers.Remove(subscriber);
                if (request.Subscribers.Count == 0 && _inflight.TryGetValue(key, out var current)
                    && ReferenceEquals(current, request))
                {
                    _inflight.Remove(key);
                    request.Cancellation.Cancel();
                }
            }
        }
    }

    private async Task<ulong> RunAsync(string key, Request request)
    {
        var token = request.Cancellation.Token;
        try
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            ulong size;
            try
            {
                size = await Task.Run(() => _measure(key, token, value => Report(request, value)), token).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                while (_cache.Count >= _capacity)
                    _cache.Remove(_cache.MinBy(pair => pair.Value.Access).Key);
                _cache[key] = new(size, _time.GetUtcNow() + Lifetime, ++_access);
            }
            SizeCached?.Invoke();
            return size;
        }
        finally
        {
            lock (_sync)
            {
                if (_inflight.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                    _inflight.Remove(key);
            }
        }
    }

    private void Report(Request request, ulong size)
    {
        Subscriber[] subscribers;
        lock (_sync) subscribers = request.Subscribers.ToArray();
        foreach (var subscriber in subscribers) subscriber.Progress?.Invoke(size);
    }

    public void Invalidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var key = Normalize(path);
        lock (_sync)
        {
            foreach (var cached in _cache.Keys.Where(p => Related(p, key)).ToArray()) _cache.Remove(cached);
            foreach (var active in _inflight.Where(p => Related(p.Key, key)).ToArray())
            {
                _inflight.Remove(active.Key);
                active.Value.Cancellation.Cancel();
            }
        }
    }

    private static bool Related(string first, string second) =>
        first.Equals(second, StringComparison.OrdinalIgnoreCase)
        || first.StartsWith(WithSeparator(second), StringComparison.OrdinalIgnoreCase)
        || second.StartsWith(WithSeparator(first), StringComparison.OrdinalIgnoreCase);

    private static string WithSeparator(string path) => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private sealed class Request
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public HashSet<Subscriber> Subscribers { get; } = [];
        public Task<ulong> Task { get; set; } = null!;
    }
    private sealed class Subscriber(Action<ulong>? progress)
    {
        public Action<ulong>? Progress { get; } = progress;
    }
    private sealed record CacheEntry(ulong Size, DateTimeOffset Expires, long Access);
}
