using FilesMate.Core.Directories;

namespace FilesMate.App.Navigation;

/// <summary>A bounded batch; overflow replaces granular changes with one refresh.</summary>
internal sealed class DirectoryWatchBuffer(int capacity)
{
    private readonly object _sync = new();
    private readonly Queue<DirectoryWatchNotification> _items = new();
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
                _overflow = false;
                _generation = notice.Generation;
            }

            if (_overflow)
                return;
            if (notice.Kind == DirectoryWatchKind.Overflow || _items.Count >= capacity)
            {
                _items.Clear();
                _overflow = true;
                notice = notice with { Kind = DirectoryWatchKind.Overflow, Name = string.Empty };
            }
            _items.Enqueue(notice);
        }
    }

    public bool TryDequeue(out DirectoryWatchNotification notice)
    {
        lock (_sync)
        {
            return _items.TryDequeue(out notice);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _items.Clear();
            _overflow = false;
        }
    }
}
