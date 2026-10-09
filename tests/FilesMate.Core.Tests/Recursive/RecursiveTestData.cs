using System.Runtime.CompilerServices;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Core.Recursive;

namespace FilesMate.Core.Tests.Recursive;

internal static class RecursiveTestData
{
    public const string Root = @"D:\recursive-fixture";
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    public static FileEntryCore File(string name, ulong size = 1, int id = 71,
        FileAttributes attributes = FileAttributes.Normal) =>
        new(id, name, size, 0, 0, attributes, EntryKind.File);

    public static FileEntryCore Directory(string name, FileAttributes attributes = FileAttributes.Directory) =>
        new(71, name, 0, 0, 0, attributes, EntryKind.Directory);

    public static DirectoryBatch Batch(DirectoryRequest request, FileEntryCore[] entries,
        bool final = true, DirectoryReadError? error = null) =>
        DirectoryBatch.Create(request.PaneId, request.Generation, request.Path, entries, final, error);

    public static RecursiveSession Session(ScriptedEnumerator enumerator, RecursiveReadOptions? options = null,
        Probe? probe = null, long generation = 7, string root = Root) =>
        new(enumerator, PaneId.New(), generation, root, options, probe ?? new Probe());

    public static ScriptedEnumerator Tree(params (string Path, FileEntryCore[] Entries)[] branches)
    {
        var tree = branches.ToDictionary(x => x.Path, x => x.Entries, StringComparer.Ordinal);
        return new ScriptedEnumerator { Read = (request, _) => Batches(Batch(request, tree[request.Path])) };
    }

    public static async Task<RecursiveSnapshot> ScanAsync(ScriptedEnumerator enumerator,
        RecursiveReadOptions? options = null, Probe? probe = null, string root = Root)
    {
        await using var session = Session(enumerator, options, probe, root: root);
        await session.RunAsync().WaitAsync(TestTimeout);
        return session.CurrentSnapshot;
    }

    public static async IAsyncEnumerable<DirectoryBatch> Batches(params DirectoryBatch[] batches)
    {
        await Task.CompletedTask;
        foreach (var batch in batches) yield return batch;
    }

    public static async IAsyncEnumerable<DirectoryBatch> Failure(Exception error)
    {
        await Task.CompletedTask;
        throw error;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    public static RecursiveProjection Project(RecursiveSnapshot snapshot, EntrySort? sort = null,
        EntryFilter? filter = null, IReadOnlySet<int>? collapsed = null,
        Func<FileEntryCore, bool>? additionalMatch = null) =>
        snapshot.BuildProjection(sort ?? EntrySort.Name, filter ?? EntryFilter.None,
            StringComparer.OrdinalIgnoreCase, collapsed, additionalMatch: additionalMatch);

    public static int Id(RecursiveSnapshot snapshot, string relativePath) =>
        Enumerable.Range(1, snapshot.Count).Single(id => snapshot.ResolveRelativePath(id) == relativePath);

    public static string[] Paths(RecursiveProjection projection) =>
        projection.Index.Select(i => projection.ResolveRelativePath(i + 1)).ToArray();

    public static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Probe : IRecursiveDirectoryProbe
    {
        public Func<string, FileAttributes> Read { get; init; } = _ => FileAttributes.Directory;
        public List<string> Paths { get; } = [];
        public FileAttributes GetAttributes(string path)
        {
            Paths.Add(path);
            return Read(path);
        }
    }

    internal sealed class ScriptedEnumerator : IDirectoryEnumerator
    {
        public required Func<DirectoryRequest, CancellationToken, IAsyncEnumerable<DirectoryBatch>> Read { get; init; }
        public List<DirectoryRequest> Requests { get; } = [];
        public int DisposeCount { get; private set; }

        public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            try
            {
                await foreach (var batch in Read(request, cancellationToken)) yield return batch;
            }
            finally { DisposeCount++; }
        }
    }
}
