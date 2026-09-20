using FilesMate.Core.Icons;

namespace FilesMate.Platform.Windows.Icons;

/// <summary>Thread-safe LRU for decoded pixels; entries do not own native handles.</summary>
internal sealed class IconBitmapCache(long maxBytes = 16L * 1024 * 1024, int maxEntries = 4096)
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, IconBitmap Bitmap)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, IconBitmap Bitmap)> _recent = new();
    private long _bytes;

    internal long CurrentBytes { get { lock (_sync) return _bytes; } }
    internal int Count { get { lock (_sync) return _entries.Count; } }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _recent.Clear();
            _bytes = 0;
        }
    }

    public bool TryGetValue(string key, out IconBitmap? bitmap)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var node)) { bitmap = null; return false; }
            _recent.Remove(node);
            _recent.AddLast(node);
            bitmap = node.Value.Bitmap;
            return true;
        }
    }

    public void Set(string key, IconBitmap bitmap)
    {
        lock (_sync)
        {
            if (_entries.Remove(key, out var previous))
            {
                _recent.Remove(previous);
                _bytes -= previous.Value.Bitmap.Bgra.LongLength;
            }
            var weight = bitmap.Bgra.LongLength;
            if (weight > maxBytes || maxEntries <= 0) return;
            while (_recent.First is { } oldest && (_bytes > maxBytes - weight || _entries.Count >= maxEntries))
            {
                _recent.RemoveFirst();
                _entries.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bitmap.Bgra.LongLength;
            }
            _entries.Add(key, _recent.AddLast((key, bitmap)));
            _bytes += weight;
        }
    }
}
