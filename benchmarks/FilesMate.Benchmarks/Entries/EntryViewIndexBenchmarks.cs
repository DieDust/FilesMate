using BenchmarkDotNet.Attributes;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Benchmarks.Entries;

[MemoryDiagnoser]
public class EntryViewIndexBenchmarks
{
    private EntryStore _store = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _store = new EntryStore();
        var batch = new FileEntryCore[Count];
        for (var i = 0; i < Count; i++)
        {
            batch[i] = new FileEntryCore(
                i + 1,
                $"item{i:D6}.txt",
                (ulong)(i % 1000),
                i,
                i,
                System.IO.FileAttributes.Normal,
                EntryKind.File);
        }

        _store.Append(batch);
    }

    [Benchmark]
    public int SortByName() =>
        EntryViewIndex.Build(_store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1).Count;

    [Benchmark]
    public int SortBySize() =>
        EntryViewIndex.Build(_store, EntrySort.Size, EntryFilter.None, NaturalStringComparer.Instance, 1).Count;

    [Benchmark]
    public int SortByType() =>
        EntryViewIndex.Build(_store, EntrySort.Type, EntryFilter.None, NaturalStringComparer.Instance, 1).Count;

    [Benchmark]
    public int SortByModified() =>
        EntryViewIndex.Build(_store, EntrySort.Modified, EntryFilter.None, NaturalStringComparer.Instance, 1).Count;

    [Benchmark]
    public int SortByCreated() =>
        EntryViewIndex.Build(_store, EntrySort.Created, EntryFilter.None, NaturalStringComparer.Instance, 1).Count;

    [Benchmark]
    [Arguments("i")]
    [Arguments("ite")]
    [Arguments("item0000")]
    public int FilterQuery(string query) =>
        EntryViewIndex.Build(
            _store,
            EntrySort.Name,
            new EntryFilter { Query = query, Match = FilterMatchKind.Prefix },
            NaturalStringComparer.Instance,
            1).Count;
}
