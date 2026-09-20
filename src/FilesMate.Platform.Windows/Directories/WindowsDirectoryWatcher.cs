using System.Runtime.CompilerServices;
using System.Threading.Channels;

using FilesMate.Core.Directories;

namespace FilesMate.Platform.Windows.Directories;

public sealed class WindowsDirectoryWatcher : IDirectoryWatcher
{
    public async IAsyncEnumerable<DirectoryWatchNotification> WatchAsync(
        DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.Path))
        {
            yield return Disabled(request);
            yield break;
        }

        var channel = Channel.CreateUnbounded<DirectoryWatchNotification>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        using var watcher = TryCreate(request.Path);
        if (watcher is null)
        {
            yield return Disabled(request);
            yield break;
        }

        void Push(DirectoryWatchKind kind, string? name, string? oldName = null) =>
            channel.Writer.TryWrite(new DirectoryWatchNotification(
                request.PaneId,
                request.Generation,
                kind,
                name ?? string.Empty,
                oldName ?? string.Empty));

        watcher.Created += (_, e) => Push(DirectoryWatchKind.Created, e.Name);
        watcher.Deleted += (_, e) => Push(DirectoryWatchKind.Deleted, e.Name);
        watcher.Changed += (_, e) => Push(DirectoryWatchKind.Modified, e.Name);
        watcher.Renamed += (_, e) => Push(DirectoryWatchKind.Renamed, e.Name, e.OldName);
        watcher.Error += (_, e) => Push(
            e.GetException() is InternalBufferOverflowException
                ? DirectoryWatchKind.Overflow
                : DirectoryWatchKind.WatcherDisabled,
            string.Empty);

        if (!TryEnable(watcher))
        {
            yield return Disabled(request);
            yield break;
        }

        await using var registration = cancellationToken.Register(() => channel.Writer.TryComplete());
        try
        {
            await foreach (var notice in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return notice;
                if (notice.Kind is DirectoryWatchKind.WatcherDisabled)
                {
                    yield break;
                }
            }
        }
        finally
        {
            watcher.EnableRaisingEvents = false;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static DirectoryWatchNotification Disabled(DirectoryRequest request) =>
        new(request.PaneId, request.Generation, DirectoryWatchKind.WatcherDisabled, string.Empty);

    private static FileSystemWatcher? TryCreate(string path)
    {
        try
        {
            return new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryEnable(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = true;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
