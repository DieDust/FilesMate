using FilesMate.Core.Entries;

namespace FilesMate.Core.Directories;

/// <summary>
/// Entry list for one directory session. Enumeration appends; live watchers may patch by name.
/// Thread-safety: mutations are serialized; snapshots seal shared blocks under the same lock.
/// Ownership: only the owning <see cref="DirectorySession"/> and its pane may mutate.
/// Staleness: entries are snapshots and may not exist on disk anymore.
/// </summary>
public sealed class EntryStore
{
    private readonly EntryBlocks _entries = new();
    private readonly object _gate = new();
    private int _nextId = 1;
    private long _dataVersion;
    private long _mutationVersion;
    private EntrySnapshot? _cachedSnapshot;

    public long DataVersion => Volatile.Read(ref _dataVersion);
    internal int AllocatedEntryCapacity { get { lock (_gate) return _entries.Capacity; } }

    // Append preserves every existing source row; all other effective mutations
    // revoke progressive snapshots before touching the live list.
    internal long MutationVersion => Volatile.Read(ref _mutationVersion);

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
            AdvanceVersion(_entries.Count, entries.Count, appendOnly: true);
            var remaining = entries.Count;
            foreach (var entry in entries)
            {
                _entries.Add(entry, remaining--);
                if (entry.Id >= _nextId)
                {
                    _nextId = entry.Id + 1;
                }
            }
        }
    }

    /// <summary>Updates one stable source row, including duplicate names from different result folders.</summary>
    public bool ReplaceAt(int sourceIndex, FileEntryCore replacement)
    {
        lock (_gate)
        {
            if ((uint)sourceIndex >= (uint)_entries.Count || _entries[sourceIndex].Id != replacement.Id) return false;
            if (_entries[sourceIndex] == replacement) return false;
            AdvanceVersion(sourceIndex, 1);
            _entries[sourceIndex] = replacement;
            return true;
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

            AdvanceVersion(index, _entries.Count - index);
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
                var replacement = entry with { Id = _entries[index].Id };
                if (replacement == _entries[index]) return;
                AdvanceVersion(index, 1);
                _entries[index] = replacement;
                return;
            }

            AdvanceVersion(_entries.Count, 1);
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
            if (source >= 0 && destination == source && replacement with { Id = _entries[source].Id } == _entries[source]) return;
            if (destination >= 0 && destination != source)
                InvalidateSnapshot(); // Removing a destination shifts the surviving positions.
            else
                AdvanceVersion(source >= 0 ? source : _entries.Count, 1);
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
            return _entries.ToArray();
        }
    }

    /// <summary>Seals the current row blocks; later writes copy only their affected blocks. Never exposes mutable aliases.</summary>
    public EntrySnapshot CaptureSnapshot(CancellationToken cancellationToken = default,
        Guid diagnosticPane = default, long generation = 0, long diagnosticOperation = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observed = FilesMate.Core.Diagnostics.PerformanceTrace.Enabled;
        var requested = observed ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long entered = 0, leaving = 0;
        var copiedRows = 0;
        var outcome = FilesMate.Core.Diagnostics.PerformanceOutcome.Completed;
        try
        {
            lock (_gate)
            {
                if (observed) entered = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_cachedSnapshot?.DataVersion == _dataVersion) return _cachedSnapshot;
                    var snapshot = EntrySnapshot.Capture(this, _dataVersion, _mutationVersion, _entries,
                        cancellationToken, out copiedRows);
                    // Install only a complete snapshot. Cancelled capture leaves
                    // prior snapshots immutable and the current rows safely writable.
                    _cachedSnapshot = snapshot;
                    return snapshot;
                }
                finally { if (observed) leaving = System.Diagnostics.Stopwatch.GetTimestamp(); }
            }
        }
        catch (OperationCanceledException) { outcome = FilesMate.Core.Diagnostics.PerformanceOutcome.Cancelled; throw; }
        catch { outcome = FilesMate.Core.Diagnostics.PerformanceOutcome.Failed; throw; }
        finally
        {
            if (observed) FilesMate.Core.Diagnostics.PerformanceTrace.Record(
                FilesMate.Core.Diagnostics.PerformanceStage.IndexLockTiming, pane: diagnosticPane,
                generation: generation, operation: diagnosticOperation, outcome: outcome,
                count: copiedRows, // Snapshot capture shares sealed row blocks; writer copy-on-write occurs at mutations.
                durationTicks: leaving == 0 ? -1 : leaving - entered,
                auxiliary: entered == 0 ? -1 : entered - requested);
        }
    }

    /// <summary>Reconciles a complete listing without replacing surviving entry identities.</summary>
    public IReadOnlyList<string> Reconcile(IReadOnlyList<FileEntryCore> listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        lock (_gate)
        {
            // Positions stay stable until the final replacement under this lock. Keep only
            // integers in the lookup instead of copying every entry into the dictionary.
            var previous = new Dictionary<string, int>(_entries.Count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _entries.Count; i++) previous.Add(_entries[i].Name, i);
            var next = new List<FileEntryCore>(listing.Count);
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var incoming in listing)
            {
                if (previous.Remove(incoming.Name, out var sourceIndex))
                {
                    var existing = _entries[sourceIndex];
                    var replacement = incoming with { Id = existing.Id };
                    if (replacement != existing) changed.Add(incoming.Name);
                    next.Add(replacement);
                }
                else
                {
                    next.Add(incoming with { Id = _nextId++ });
                    changed.Add(incoming.Name);
                }
            }
            foreach (var removed in previous.Keys) changed.Add(removed);
            // Keep the original positions when the filesystem merely enumerates in a different order.
            if (changed.Count == 0) return [];
            InvalidateSnapshot();
            _entries.Clear();
            _entries.AddRange(next);
            return changed.ToArray();
        }
    }

    /// <summary>
    /// Observes the live list under the store lock. Only use for short synchronous observations.
    /// Filtering, sorting and UI row reads use immutable snapshots. Do not mutate in the callback.
    /// </summary>
    public T Observe<T>(Func<IReadOnlyList<FileEntryCore>, T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            return observer(_entries);
        }
    }

    private void AdvanceVersion(int firstChanged, int changedCount, bool appendOnly = false)
    {
        // Mark old snapshots stale before changing any live row. Mutations already hold _gate.
        if (!appendOnly) Volatile.Write(ref _mutationVersion, _mutationVersion + 1);
        Volatile.Write(ref _dataVersion, _dataVersion + 1);

    }

    private void InvalidateSnapshot()
    {
        Volatile.Write(ref _mutationVersion, _mutationVersion + 1);
        Volatile.Write(ref _dataVersion, _dataVersion + 1);
        _cachedSnapshot = null;
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
