using FilesMate.Core.Entries;

namespace FilesMate.Core.Directories;

/// <summary>
/// Entry list for one directory session. Enumeration appends; live watchers may patch by name.
/// Thread-safety: mutations are serialized; snapshots copy under the same lock.
/// Ownership: only the owning <see cref="DirectorySession"/> and its pane may mutate.
/// Staleness: entries are snapshots and may not exist on disk anymore.
/// </summary>
public sealed class EntryStore
{
    private readonly List<FileEntryCore> _entries = [];
    private readonly object _gate = new();
    private int _nextId = 1;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public FileEntryCore this[int index]
    {
        get
        {
            lock (_gate)
            {
                return _entries[index];
            }
        }
    }

    public void Append(IReadOnlyList<FileEntryCore> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var entry in entries)
            {
                _entries.Add(entry);
                if (entry.Id >= _nextId)
                {
                    _nextId = entry.Id + 1;
                }
            }
        }
    }

    public bool RemoveByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        lock (_gate)
        {
            var index = IndexOfName(name);
            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
            return true;
        }
    }

    public void Upsert(FileEntryCore entry)
    {
        lock (_gate)
        {
            var index = IndexOfName(entry.Name);
            if (index >= 0)
            {
                _entries[index] = entry with { Id = _entries[index].Id };
                return;
            }

            _entries.Add(entry with { Id = _nextId++ });
        }
    }

    public void Rename(string oldName, FileEntryCore replacement)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldName);
        lock (_gate)
        {
            var destination = IndexOfName(replacement.Name);
            var source = IndexOfName(oldName);
            if (destination >= 0 && destination != source)
            {
                _entries.RemoveAt(destination);
                if (source > destination)
                {
                    source--;
                }
            }

            if (source >= 0)
            {
                _entries[source] = replacement with { Id = _entries[source].Id };
                return;
            }

            _entries.Add(replacement with { Id = _nextId++ });
        }
    }

    public FileEntryCore[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }

    /// <summary>
    /// Observes the live list under the store lock. Used to filter and sort indexes without copying entry structs
    /// or taking a lock per comparison. Do not mutate from inside <paramref name="observer"/>.
    /// </summary>
    public T Observe<T>(Func<IReadOnlyList<FileEntryCore>, T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            return observer(_entries);
        }
    }

    private int IndexOfName(string name)
    {
        // ponytail: O(n) by name; folder listings stay in the low thousands. Hash map if traces show this.
        for (var i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
