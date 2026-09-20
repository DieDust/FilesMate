using System.Runtime.CompilerServices;
using System.Threading.Channels;

using FilesMate.Core.Directories;

namespace FilesMate.App.Tests.Navigation;

internal sealed class FakeDirectoryWatcher : IDirectoryWatcher
{
    public Channel<DirectoryWatchNotification> Notifications { get; } =
        Channel.CreateUnbounded<DirectoryWatchNotification>();

    public int Started { get; private set; }

    public async IAsyncEnumerable<DirectoryWatchNotification> WatchAsync(
        DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Started++;
        await foreach (var notice in Notifications.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return notice with { PaneId = request.PaneId, Generation = request.Generation };
        }
    }

    public ValueTask DisposeAsync()
    {
        Notifications.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
