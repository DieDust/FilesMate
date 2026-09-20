using System.Runtime.CompilerServices;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

internal sealed class FakeDirectoryEnumerator : IDirectoryEnumerator
{
    public Dictionary<string, FileEntryCore[]> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, DirectoryReadError> Errors { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Func<DirectoryRequest, Task>? BeforeFirstBatch { get; set; }

    public bool EmitStaleGeneration { get; set; }

    public int Started { get; private set; }

    public int Canceled { get; private set; }

    public List<long> Generations { get; } = [];

    public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
        DirectoryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Started++;
        Generations.Add(request.Generation);
        List<DirectoryBatch> batches;
        try
        {
            if (BeforeFirstBatch is not null)
            {
                await BeforeFirstBatch(request).WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            batches = CreateBatches(request);
        }
        catch (OperationCanceledException)
        {
            Canceled++;
            yield break;
        }

        foreach (var batch in batches)
        {
            yield return batch;
        }
    }

    private List<DirectoryBatch> CreateBatches(DirectoryRequest request)
    {
        var batches = new List<DirectoryBatch>();
        if (EmitStaleGeneration && request.Generation > 0)
        {
            batches.Add(DirectoryBatch.Create(
                request.PaneId,
                Math.Max(0, request.Generation - 1),
                request.Path,
                [Entry(99, "stale.txt")],
                isFinal: false,
                error: null));
        }

        if (Errors.TryGetValue(request.Path, out var error))
        {
            batches.Add(DirectoryBatch.Create(request.PaneId, request.Generation, request.Path, [], isFinal: true, error));
            return batches;
        }

        if (!Folders.TryGetValue(request.Path, out var entries))
        {
            batches.Add(DirectoryBatch.Create(
                request.PaneId,
                request.Generation,
                request.Path,
                [],
                isFinal: true,
                new DirectoryReadError(DirectoryReadErrorKind.NotFound, 2, "Path not found.", isTerminal: true)));
            return batches;
        }

        batches.Add(DirectoryBatch.Create(request.PaneId, request.Generation, request.Path, entries, isFinal: true, error: null));
        return batches;
    }

    public static FileEntryCore Entry(int id, string name, EntryKind kind = EntryKind.File) =>
        new(id, name, Size: 1, ModifiedUtcTicks: 1, CreatedUtcTicks: 1, Attributes: FileAttributes.Normal, Kind: kind);
}
