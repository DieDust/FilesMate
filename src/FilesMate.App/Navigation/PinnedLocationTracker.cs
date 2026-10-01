using FilesMate.Platform.Windows.Metadata;

namespace FilesMate.App.Navigation;

/// <summary>Observe external renames and recover local pins by identity, including after restart.</summary>
public sealed class PinnedLocationTracker : IDisposable
{
    private readonly PinnedLocationStore _store;
    private readonly Action<Action> _dispatch;
    private readonly Action _changed;
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private int _checking;
    private volatile bool _disposed;

    public PinnedLocationTracker(PinnedLocationStore store, Action<Action> dispatch, Action changed)
    {
        _store = store; _dispatch = dispatch; _changed = changed;
        _timer = new Timer(CheckReferences, null, Timeout.Infinite, 1500);
        Refresh();
        CheckNow();
    }

    // Called on the owner's UI dispatcher along with pin edits and writes.
    public void Refresh()
    {
        if (_disposed) return;
        _store.RememberFolderReferences();
        var parents = _store.Load().Select(Path.GetDirectoryName).Where(parent => !string.IsNullOrEmpty(parent))
            .Select(parent => parent!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in _watchers.Keys.Where(parent => !parents.Contains(parent)).ToArray())
        {
            _watchers[parent].Dispose(); _watchers.Remove(parent);
        }
        foreach (var parent in parents.Where(parent => !_watchers.ContainsKey(parent)))
        {
            try
            {
                var watcher = new FileSystemWatcher(parent) { NotifyFilter = NotifyFilters.DirectoryName, IncludeSubdirectories = false };
                watcher.Renamed += OnRenamed;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(parent, watcher);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    public void CheckNow()
    {
        if (_disposed) return;
        try { _timer.Change(0, 1500); }
        catch (ObjectDisposedException) { }
    }

    private void OnRenamed(object sender, RenamedEventArgs args) => _dispatch(() =>
    {
        if (_disposed) return;
        var references = _store.LoadState().FolderReferences ?? [];
        foreach (var reference in references)
        {
            var suffix = PinnedLocationStore.PathsEqual(reference.Path, args.OldFullPath) ? ""
                : reference.Path.StartsWith(args.OldFullPath + "\\", StringComparison.OrdinalIgnoreCase) ? reference.Path[args.OldFullPath.Length..] : null;
            if (suffix is not null && WindowsDirectoryReferences.Capture(args.FullPath + suffix) != reference.Identity)
            { CheckNow(); return; }
        }
        Relocate(args.OldFullPath, args.FullPath);
    });

    private void CheckReferences(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _checking, 1) != 0) return;
        try
        {
            var pins = _store.LoadState();
            foreach (var reference in pins.FolderReferences ?? [])
            {
                if (_disposed) break;
                var resolved = WindowsDirectoryReferences.Resolve(reference.Identity);
                if (resolved is not null && !string.Equals(reference.Path, resolved, StringComparison.Ordinal))
                    _dispatch(() =>
                    {
                        if (_store.LoadState().FolderReferences?.Any(current => current == reference) == true)
                            Relocate(reference.Path, resolved);
                    });
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        finally { Volatile.Write(ref _checking, 0); }
    }

    private void Relocate(string oldPath, string newPath)
    {
        if (_disposed) return;
        try { if (_store.Relocate(oldPath, newPath)) _changed(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        _watchers.Clear();
    }
}
