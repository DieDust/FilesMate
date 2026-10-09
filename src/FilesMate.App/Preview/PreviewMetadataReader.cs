using System.IO;

namespace FilesMate.App.Preview;

/// <summary>Bounded filesystem work shared by every preview surface and content type.</summary>
internal sealed class PreviewMetadataReader
{
    internal static PreviewMetadataReader Shared { get; } = new();
    private readonly SemaphoreSlim _workers;
    private readonly Func<string, CancellationToken, PreviewResult.Properties> _read;

    internal PreviewMetadataReader(
        Func<string, CancellationToken, PreviewResult.Properties>? read = null,
        int concurrency = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        _workers = new SemaphoreSlim(concurrency, concurrency);
        _read = read ?? Read;
    }

    internal async Task<PreviewResult.Properties> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await _workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        var work = Task.Run(() => _read(path, cancellationToken), cancellationToken);
        try
        {
            var result = await work.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            // Cancellation releases the caller promptly, but an uninterruptible
            // OS read still owns its physical slot until it actually finishes.
            if (work.IsCompleted)
            {
                _ = work.Exception; // A fault can race a caller's cancellation.
                _workers.Release();
            }
            else _ = ReleaseWorkerAsync(work);
        }
    }

    private async Task ReleaseWorkerAsync(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception) { /* Observe failures from work whose caller has left. */ }
        finally { _workers.Release(); }
    }

    private static PreviewResult.Properties Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Directory.Exists(path))
        {
            token.ThrowIfCancellationRequested();
            var directory = new DirectoryInfo(path);
            var result = new PreviewResult.Properties(path, 0,
                directory.Exists ? directory.LastWriteTimeUtc : null,
                directory.Exists ? directory.Attributes : 0,
                IsDirectory: true,
                ChildCount: CountChildren(directory, token),
                CreationTimeUtc: directory.Exists ? directory.CreationTimeUtc : null);
            token.ThrowIfCancellationRequested();
            return result;
        }

        token.ThrowIfCancellationRequested();
        var info = new FileInfo(path);
        var file = new PreviewResult.Properties(path,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTimeUtc : null,
            info.Exists ? info.Attributes : 0,
            CreationTimeUtc: info.Exists ? info.CreationTimeUtc : null);
        token.ThrowIfCancellationRequested();
        return file;
    }

    private static int? CountChildren(DirectoryInfo directory, CancellationToken token)
    {
        try
        {
            var count = 0;
            using var entries = directory.EnumerateFileSystemInfos().GetEnumerator();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!entries.MoveNext()) return count;
                token.ThrowIfCancellationRequested();
                if (++count > 500) return 501;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            token.ThrowIfCancellationRequested();
            return null;
        }
    }
}
