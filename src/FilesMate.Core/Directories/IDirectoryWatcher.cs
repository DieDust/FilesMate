using FilesMate.Core.Navigation;

namespace FilesMate.Core.Directories;

/// <summary>
/// Event-based change notification for an active pane. Watchers exist only for active sessions.
/// Thread-safety: implementations must not invoke filesystem APIs on the UI thread.
/// Ownership: disposing the watcher stops I/O and releases the native handle.
/// Cancellation: session disposal and <paramref name="cancellationToken"/> both stop watching.
/// Errors: overflow or unsupported locations should surface as a refresh request rather than a crash.
/// Staleness: events can race with operations and must be matched by pane and generation.
/// </summary>
public interface IDirectoryWatcher : IAsyncDisposable
{
    public IAsyncEnumerable<DirectoryWatchNotification> WatchAsync(
        DirectoryRequest request,
        CancellationToken cancellationToken);
}

public readonly record struct DirectoryWatchNotification(
    PaneId PaneId,
    long Generation,
    DirectoryWatchKind Kind,
    string Name,
    string OldName = "");

public enum DirectoryWatchKind
{
    Created = 0,
    Deleted = 1,
    Modified = 2,
    Renamed = 3,
    Overflow = 4,
    WatcherDisabled = 5,
}
