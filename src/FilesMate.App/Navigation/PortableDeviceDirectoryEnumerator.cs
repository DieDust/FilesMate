using System.Runtime.CompilerServices;
using FilesMate.App.Localization;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Navigation;

/// <summary>Adapts a device folder to the same cancellable pane session used by disk folders.</summary>
public sealed class PortableDeviceDirectoryEnumerator(
    IDirectoryEnumerator inner,
    Func<PortableDeviceLocation, CancellationToken, Task<IReadOnlyList<PortableDeviceEntry>>> read,
    Func<PortableDeviceLocation, CancellationToken, Task<DeviceFolderCapabilities>> capabilities) : IDirectoryEnumerator
{
    private sealed record Snapshot(string Path, long Generation, bool Writable, Dictionary<int, string> Locations);
    private readonly object _gate = new();
    private Snapshot? _snapshot;
    private long _requestVersion;
    private long _latestGeneration = -1;

    public string? Resolve(string path, long generation, int id)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot?.Path == path && snapshot.Generation == generation
            && snapshot.Locations.TryGetValue(id, out var uri) ? uri : null;
    }

    public bool CanReceiveFiles(string path, long generation)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return snapshot?.Path == path && snapshot.Generation == generation && snapshot.Writable;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _requestVersion++;
            Volatile.Write(ref _snapshot, null);
        }
    }

    private long BeginRequest(long generation)
    {
        lock (_gate)
        {
            if (generation < _latestGeneration) return -1;
            _latestGeneration = generation;
            Volatile.Write(ref _snapshot, null);
            return ++_requestVersion;
        }
    }

    private bool Publish(long version, Snapshot snapshot)
    {
        lock (_gate)
        {
            if (version != _requestVersion) return false;
            Volatile.Write(ref _snapshot, snapshot);
            return true;
        }
    }

    public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var version = BeginRequest(request.Generation);
        if (version < 0) yield break;
        if (!PortableDeviceLocation.TryParse(request.Path, out var location))
        {
            await foreach (var batch in inner.EnumerateAsync(request, cancellationToken).ConfigureAwait(false))
                yield return batch;
            yield break;
        }

        List<FileEntryCore> entries = [];
        Dictionary<int, string> locations = [];
        DirectoryReadError? error = null;
        var writable = false;
        try
        {
            var children = await read(location, cancellationToken).ConfigureAwait(false);
            writable = (await capabilities(location, cancellationToken).ConfigureAwait(false)).CanReceiveFiles;
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = entries.Count + 1;
                locations.Add(id, child.Location.Uri);
                entries.Add(new(id, child.Name, (ulong)Math.Max(0, child.Size ?? 0),
                    child.Modified?.ToUniversalTime().Ticks ?? 0, 0,
                    child.IsFolder ? FileAttributes.Directory : FileAttributes.Normal,
                    child.IsFolder ? EntryKind.Directory : EntryKind.File));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = new(DirectoryReadErrorKind.Unknown, ex.HResult, StringTable.Get("Device_Unavailable"), isTerminal: true);
            entries.Clear();
            locations.Clear();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!Publish(version, new(request.Path, request.Generation, writable, locations))) yield break;
        yield return DirectoryBatch.Create(request.PaneId, request.Generation, request.Path, entries, true, error);
    }
}
