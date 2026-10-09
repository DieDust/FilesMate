namespace FilesMate.App.Services;

/// <summary>
/// App-lifetime MRU state. Navigation only updates a bounded memory snapshot;
/// one disk worker and one independent, serialized shell publisher coalesce changes.
/// </summary>
internal sealed class RecentFolderHistory
{
    private readonly object _sync = new();
    private readonly Func<IReadOnlyList<string>> _load;
    private readonly Action<IReadOnlyList<string>> _save;
    private readonly Func<IReadOnlyList<string>, Task>? _publish;
    private readonly Action<Exception> _reportFailure;
    private string[] _folders = [];
    private (string Path, long Revision)[] _pendingVisits = [];
    private bool _loaded;
    private long _revision;
    private string[]? _publicationFolders;
    private long _publicationRevision;
    private long _publishedRevision = -1;
    private Task? _persistenceTask;
    private Task? _publicationTask;

    public RecentFolderHistory(RecentFolderStore store, Func<IReadOnlyList<string>, Task>? publish = null)
        : this(store.Read, store.Save, publish,
            error => System.Diagnostics.Trace.TraceError("Recent folder update failed: {0}", error))
    {
    }

    internal RecentFolderHistory(Func<IReadOnlyList<string>> load, Action<IReadOnlyList<string>> save,
        Func<IReadOnlyList<string>, Task>? publish = null, Action<Exception>? reportFailure = null)
    {
        _load = load;
        _save = save;
        _publish = publish;
        _reportFailure = reportFailure ?? (_ => { });
    }

    public Task InitializeAsync() => FlushAsync();

    public IReadOnlyList<string> Record(string? path)
    {
        var normalized = RecentFolderStore.NormalizePath(path);
        lock (_sync)
        {
            // Identity is case-insensitive, but a differently cased visit must not
            // change the display spelling of an entry we already know.
            normalized = _folders.FirstOrDefault(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                ?? normalized;
            if (normalized is not null
                && (_folders.Length == 0 || !string.Equals(_folders[0], normalized, StringComparison.OrdinalIgnoreCase)))
            {
                _folders = _folders.Where(item => !string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                    .Prepend(normalized).Take(RecentFolderStore.MaxEntries).ToArray();
            }

            if (normalized is not null
                && (_pendingVisits.Length == 0 || !string.Equals(_pendingVisits[0].Path, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                _revision++;
                _pendingVisits = _pendingVisits.Where(item => !string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase))
                    .Prepend((normalized, _revision)).Take(RecentFolderStore.MaxEntries).ToArray();
            }

            QueuePersistenceLocked();
            return Array.AsReadOnly(_folders);
        }
    }

    /// <summary>
    /// Attempts to persist all current changes, including changes made during a write.
    /// Failures are logged and retained for the next record/flush. Does not wait for
    /// the optional native shell publisher; shutdown needs no UI dispatcher callbacks.
    /// </summary>
    public Task FlushAsync()
    {
        lock (_sync)
        {
            QueuePersistenceLocked();
            return _persistenceTask ?? Task.CompletedTask;
        }
    }

    internal Task WaitForPublicationAsync()
    {
        lock (_sync) return _publicationTask ?? Task.CompletedTask;
    }

    private void QueuePersistenceLocked()
    {
        if (_persistenceTask is null && (!_loaded || _pendingVisits.Length > 0))
        {
            _persistenceTask = Task.Run(PersistPending);
        }
    }

    private void PersistPending()
    {
        while (true)
        {
            long revision;
            lock (_sync)
            {
                revision = _revision;
            }

            IReadOnlyList<string> persisted;
            try
            {
                // Another app process can share this profile. Preserve the legacy
                // read-before-write merge, off UI and once per coalesced batch.
                // Simultaneous processes remain last-writer-wins, as before.
                persisted = _load();
            }
            catch (Exception error)
            {
                ReportFailure(error);
                lock (_sync)
                {
                    if (_revision != revision) continue;
                    _persistenceTask = null;
                    return;
                }
            }

            string[] snapshot;
            lock (_sync)
            {
                // Merge only unpersisted local visits, not the old cached tail.
                // Visits arriving during the read are newer than persisted history.
                // Fresh persisted spelling wins for known identities; genuinely new
                // entries retain their first local spelling until persisted.
                var visits = _pendingVisits.Select(item => persisted.FirstOrDefault(
                    path => string.Equals(path, item.Path, StringComparison.OrdinalIgnoreCase)) ?? item.Path);
                _folders = RecentFolderStore.Normalize(visits.Concat(persisted));
                _loaded = true;
                revision = _revision;
                snapshot = _folders;
                QueuePublicationLocked();
            }

            var saved = false;
            try
            {
                if (!snapshot.SequenceEqual(persisted, StringComparer.OrdinalIgnoreCase))
                {
                    _save(Array.AsReadOnly(snapshot));
                }
                saved = true;
            }
            catch (Exception error)
            {
                ReportFailure(error);
            }

            lock (_sync)
            {
                if (saved) _pendingVisits = _pendingVisits.Where(item => item.Revision > revision).ToArray();
                if (_revision != revision) continue;
                _persistenceTask = null;
                return;
            }
        }
    }

    private void QueuePublicationLocked()
    {
        if (_publish is null) return;
        if (_publicationFolders is null || !_publicationFolders.SequenceEqual(_folders, StringComparer.Ordinal))
        {
            _publicationFolders = _folders;
            _publicationRevision++;
        }

        if (_publicationTask is null && _publishedRevision != _publicationRevision)
        {
            _publicationTask = Task.Run(PublishPendingAsync);
        }
    }

    private async Task PublishPendingAsync()
    {
        while (true)
        {
            string[] snapshot;
            long revision;
            lock (_sync)
            {
                snapshot = _publicationFolders!;
                revision = _publicationRevision;
            }

            var published = false;
            try
            {
                await _publish!(Array.AsReadOnly(snapshot)).ConfigureAwait(false);
                published = true;
            }
            catch (Exception error)
            {
                ReportFailure(error);
            }

            lock (_sync)
            {
                if (published) _publishedRevision = revision;
                if (_publicationRevision != revision) continue;
                _publicationTask = null;
                return;
            }
        }
    }

    private void ReportFailure(Exception error)
    {
        // Optional diagnostics must never fault either background worker.
        try { _reportFailure(error); }
        catch (Exception) { }
    }
}
