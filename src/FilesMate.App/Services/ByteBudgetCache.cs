namespace FilesMate.App.Services;

/// <summary>Shared cache with byte and entry limits; reading an item keeps it hot.</summary>
internal sealed class ByteBudgetCache<T>(long maxBytes, int maxEntries, Func<long>? clock = null) where T : class
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _leases = new(StringComparer.Ordinal);
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private long _bytes, _hits, _misses;
    private long _retainedBytes;
    private long _nextLeaseExpiry = long.MaxValue;
    private readonly record struct Entry(string Key, T Value, long Bytes, long LeaseOwner = 0, long LeaseUntil = 0, bool LeaseOnly = false);

    internal (int Count, long Bytes, long Hits, long Misses) Statistics
    {
        get { lock (_sync) return (_entries.Count, _bytes, _hits, _misses); }
    }

    internal (int Count, long Bytes) RetentionStatistics
    {
        get
        {
            lock (_sync)
            {
                PruneExpiredCore(_clock());
                return (_leases.Count, _retainedBytes);
            }
        }
    }

    internal long NextRetentionExpiry
    {
        get { lock (_sync) return _nextLeaseExpiry; }
    }

    public bool TryGetValue(string key, out T value)
    {
        lock (_sync)
        {
            PruneExpiredCore(_clock());
            if (!_entries.TryGetValue(key, out var node))
            {
                _misses++;
                value = null!;
                return false;
            }
            _hits++;
            _recent.Remove(node);
            _recent.AddLast(node);
            // A real cache request now owns normal LRU residency. Releasing a
            // previous off-screen lease must not remove a newly requested entry.
            if (node.Value.LeaseOnly) node.Value = node.Value with { LeaseOnly = false };
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string key, T value, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_sync)
        {
            PruneExpiredCore(_clock());
            SetCore(new Entry(key, value, bytes));
        }
    }

    public bool Retain(string key, T value, long bytes, long owner, int milliseconds,
        long maxRetainedBytes, int maxRetainedEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (owner == 0 || milliseconds <= 0 || bytes > maxBytes || bytes > maxRetainedBytes
            || maxEntries <= 0 || maxRetainedEntries <= 0) return false;
        lock (_sync)
        {
            var now = _clock();
            PruneExpiredCore(now);
            var leaseOnly = !_entries.TryGetValue(key, out var previous) || previous.Value.LeaseOnly;
            if (previous is not null) Remove(previous);
            while (_retainedBytes > maxRetainedBytes - bytes || _leases.Count >= maxRetainedEntries)
            {
                var oldest = _leases.Values.MinBy(node => node.Value.LeaseUntil)!;
                ReleaseLease(oldest);
            }
            SetCore(new Entry(key, value, bytes, owner, now + milliseconds, leaseOnly));
            return true;
        }
    }

    public void ReleaseRetained(long owner)
    {
        lock (_sync)
            foreach (var node in _leases.Values.Where(node => node.Value.LeaseOwner == owner).ToArray()) ReleaseLease(node);
    }

    public void PruneExpired()
    {
        lock (_sync) PruneExpiredCore(_clock());
    }

    private void PruneExpiredCore(long now)
    {
        if (now < _nextLeaseExpiry) return;
        foreach (var node in _leases.Values.Where(node => node.Value.LeaseUntil <= now).ToArray()) ReleaseLease(node);
    }

    private void ReleaseLease(LinkedListNode<Entry> node)
    {
        if (node.Value.LeaseOnly) Remove(node);
        else
        {
            _leases.Remove(node.Value.Key);
            _retainedBytes -= node.Value.Bytes;
            node.Value = node.Value with { LeaseOwner = 0, LeaseUntil = 0 };
            UpdateLeaseExpiry();
        }
    }

    private void SetCore(Entry entry)
    {
        if (_entries.TryGetValue(entry.Key, out var old)) Remove(old);
        if (entry.Bytes > maxBytes || maxEntries <= 0) return;
        while (_recent.First is { } first && (_bytes > maxBytes - entry.Bytes || _entries.Count >= maxEntries))
        {
            var victim = first;
            while (victim.Value.LeaseUntil != 0 && victim.Next is { } next) victim = next;
            // The total budget wins even when every entry has a short lease.
            if (victim.Value.LeaseUntil != 0) victim = first;
            Remove(victim);
        }
        var added = _recent.AddLast(entry);
        _entries.Add(entry.Key, added);
        _bytes += entry.Bytes;
        if (entry.LeaseUntil != 0)
        {
            _leases.Add(entry.Key, added);
            _retainedBytes += entry.Bytes;
            UpdateLeaseExpiry();
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _recent.Remove(node);
        _bytes -= node.Value.Bytes;
        if (node.Value.LeaseUntil != 0)
        {
            _leases.Remove(node.Value.Key);
            _retainedBytes -= node.Value.Bytes;
            UpdateLeaseExpiry();
        }
    }

    private void UpdateLeaseExpiry() => _nextLeaseExpiry = _leases.Count == 0
        ? long.MaxValue : _leases.Values.Min(node => node.Value.LeaseUntil);

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _recent.Clear();
            _bytes = 0;
            _retainedBytes = 0;
            _leases.Clear();
            _nextLeaseExpiry = long.MaxValue;
        }
    }
}
