namespace FilesMate.Core.Directories;

/// <summary>
/// Streams directory entries as cancellable batches. Implementations must not run on a UI thread.
/// Thread-safety: a single request should be consumed by one enumerator; the enumerator is not required to be re-entrant.
/// Ownership: the enumerator does not own the destination store.
/// Cancellation: honor <paramref name="cancellationToken"/> between native iterations and before each publish.
/// Errors: terminal errors complete the sequence with a final batch; partial errors may include entries.
/// Staleness: emitted entries may already be gone from disk.
/// </summary>
public interface IDirectoryEnumerator
{
    public IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
        DirectoryRequest request,
        CancellationToken cancellationToken);
}
