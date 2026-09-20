namespace FilesMate.App.Services;

/// <summary>Shared cache with byte and entry limits; reading an item keeps it hot.</summary>
internal sealed class ByteBudgetCache<T>(long maxBytes, int maxEntries) where T : class
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, T Value, long Bytes)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, T Value, long Bytes)> _recent = new();
    private long _bytes, _hits, _misses;

    internal (int Count, long Bytes, long Hits, long Misses) Statistics
    {
        get { lock (_sync) return (_entries.Count, _bytes, _hits, _misses); }
    }

    public bool TryGetValue(string key, out T value)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                _misses++;
                value = null!;
                return false;
            }
            _hits++;
            _recent.Remove(node);
            _recent.AddLast(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(string key, T value, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_sync)
        {
            if (_entries.Remove(key, out var old))
            {
                _recent.Remove(old);
                _bytes -= old.Value.Bytes;
            }
            if (bytes > maxBytes || maxEntries <= 0) return;
            while (_recent.First is { } first && (_bytes > maxBytes - bytes || _entries.Count >= maxEntries))
            {
                _recent.RemoveFirst();
                _entries.Remove(first.Value.Key);
                _bytes -= first.Value.Bytes;
            }
            _entries.Add(key, _recent.AddLast((key, value, bytes)));
            _bytes += bytes;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _recent.Clear();
            _bytes = 0;
        }
    }
}
