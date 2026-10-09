namespace FilesMate.App.Services;

/// <summary>
/// Bounded discovery of objects owned elsewhere. Lookup never extends ownership
/// beyond the returned local reference, and does not promote into a strong LRU.
/// </summary>
internal sealed class WeakValueCache<T>(int maxEntries, long maxKeyBytes) where T : class
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recent = new();
    private long _keyBytes, _hits, _misses;
    private int _writesUntilSweep = 32;

    internal (int Count, long KeyBytes, long Hits, long Misses) Statistics
    {
        get { lock (_sync) return (_entries.Count, _keyBytes, _hits, _misses); }
    }

    public bool TryGetValue(string key, out T value)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                if (node.Value.Value.TryGetTarget(out value!))
                {
                    _recent.Remove(node);
                    _recent.AddLast(node);
                    _hits++;
                    return true;
                }
                Remove(node);
            }
            _misses++;
            value = null!;
            return false;
        }
    }

    public void Set(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var previous)) Remove(previous);
            if (--_writesUntilSweep == 0)
            {
                SweepCore();
                _writesUntilSweep = 32;
            }
            var bytes = key.Length * 2L;
            if (maxEntries <= 0 || bytes > maxKeyBytes) return;
            while (_recent.First is { } oldest && (_entries.Count >= maxEntries || _keyBytes > maxKeyBytes - bytes))
                Remove(oldest);
            _entries.Add(key, _recent.AddLast(new Entry(key, new WeakReference<T>(value), bytes)));
            _keyBytes += bytes;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _recent.Clear();
            _keyBytes = 0;
            _writesUntilSweep = 32;
        }
    }

    internal void Sweep()
    {
        lock (_sync) SweepCore();
    }

    private void SweepCore()
    {
        var node = _recent.First;
        while (node is not null)
        {
            var next = node.Next;
            if (!node.Value.Value.TryGetTarget(out _)) Remove(node);
            node = next;
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _entries.Remove(node.Value.Key);
        _recent.Remove(node);
        _keyBytes -= node.Value.Bytes;
    }

    private sealed record Entry(string Key, WeakReference<T> Value, long Bytes);
}
