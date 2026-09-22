using FilesMate.Core.Directories;

namespace FilesMate.App.Navigation;

/// <summary>Coalesces pending metadata changes; overflow requests one refresh.</summary>
internal sealed class DirectoryWatchBuffer(int capacity)
{
    private readonly object _sync = new();
    private readonly LinkedList<DirectoryWatchNotification> _items = new();
    private readonly Dictionary<string, LinkedListNode<DirectoryWatchNotification>> _modified = new(StringComparer.Ordinal);
    private bool _overflow;
    private long _generation = long.MinValue;

    public bool IsEmpty
    {
        get { lock (_sync) return _items.Count == 0; }
    }

    public void Enqueue(DirectoryWatchNotification notice)
    {
        lock (_sync)
        {
            if (notice.Generation < _generation)
                return;
            if (notice.Generation > _generation)
            {
                _items.Clear();
                _modified.Clear();
                _overflow = false;
                _generation = notice.Generation;
            }

            if (_overflow)
                return;
            // Metadata is read when the batch is consumed, so a second pending
            // change for this exact name adds no information. Structural events
            // are ordering barriers: delete/recreate and rename must stay intact.
            if (notice.Kind == DirectoryWatchKind.Modified)
            {
                if (_modified.ContainsKey(notice.Name)) return;
            }
            else
            {
                _modified.Remove(notice.Name);
                if (!string.IsNullOrEmpty(notice.OldName)) _modified.Remove(notice.OldName);
            }
            if (notice.Kind == DirectoryWatchKind.Overflow || _items.Count >= capacity)
            {
                _items.Clear();
                _modified.Clear();
                _overflow = true;
                notice = notice with { Kind = DirectoryWatchKind.Overflow, Name = string.Empty };
            }
            var node = _items.AddLast(notice);
            if (notice.Kind == DirectoryWatchKind.Modified) _modified[notice.Name] = node;
        }
    }

    public bool TryDequeue(out DirectoryWatchNotification notice)
    {
        lock (_sync)
        {
            if (_items.First is not { } node)
            {
                notice = default;
                return false;
            }
            notice = node.Value;
            _items.RemoveFirst();
            if (_modified.TryGetValue(notice.Name, out var pending) && ReferenceEquals(pending, node))
                _modified.Remove(notice.Name);
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            _modified.Clear();
            _overflow = false;
        }
    }
}
