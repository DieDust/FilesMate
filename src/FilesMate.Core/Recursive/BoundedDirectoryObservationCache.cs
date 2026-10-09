namespace FilesMate.Core.Recursive;

/// <summary>Per-scan watcher hints only. Missing/evicted keys are unknown, never evidence of an unchanged directory.</summary>
public sealed class BoundedDirectoryObservationCache<T>
{
    public const long DefaultMaximumBytes = 4L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _recent = new();
    private readonly int _maximumEntries;
    private readonly int _valueBytes;
    private readonly long _containerBytes;
    private long _bytes, _characters, _evictions;
    private readonly record struct Entry(string Path, T Value, long Bytes);

    public BoundedDirectoryObservationCache(long maximumBytes = DefaultMaximumBytes, int maximumEntries = 4096, int estimatedValueBytes = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(estimatedValueBytes);
        MaximumBytes = maximumBytes;
        _maximumEntries = maximumEntries;
        _valueBytes = estimatedValueBytes;
        _entries = new(maximumEntries, StringComparer.OrdinalIgnoreCase);
        // Account the actual dictionary allocation, including its unused slots.
        _containerBytes = 128 + 48L * _entries.EnsureCapacity(0);
        if (_containerBytes >= maximumBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _bytes = _containerBytes;
    }

    public long MaximumBytes { get; }
    public (int Count, long EstimatedBytes, long PathCharacters, long Evictions) Statistics
    {
        get { lock (_gate) return (_entries.Count, _bytes, _characters, _evictions); }
    }

    /// <summary>Keep the first fingerprint until eviction. Re-observation must not bless a changed identity.</summary>
    public bool TryAdd(string path, T value)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var existing)) { Touch(existing); return false; }
            var bytes = 128L + _valueBytes + ((24L + 2L * path.Length + 7) & ~7L);
            if (bytes > MaximumBytes - _containerBytes) return false;
            LinkedListNode<Entry>? reusable = null;
            while (_entries.Count >= _maximumEntries || bytes > MaximumBytes - _bytes)
            {
                var oldest = _recent.First!;
                _recent.RemoveFirst();
                _entries.Remove(oldest.Value.Path);
                _bytes -= oldest.Value.Bytes;
                _characters -= oldest.Value.Path.Length;
                _evictions++;
                reusable ??= oldest;
            }
            var entry = new Entry(path, value, bytes);
            var node = reusable ?? new LinkedListNode<Entry>(entry);
            node.Value = entry;
            _recent.AddLast(node);
            _entries.Add(path, node);
            _bytes += bytes;
            _characters += path.Length;
            return true;
        }
    }

    public bool TryGetValue(string path, out T value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var node)) { Touch(node); value = node.Value.Value; return true; }
            value = default!;
            return false;
        }
    }

    private void Touch(LinkedListNode<Entry> node) { _recent.Remove(node); _recent.AddLast(node); }
}

public interface IRecursiveObservationMemory
{
    public long ObservationByteLimit { get; }
    public long EstimatedObservationBytes { get; }
}
