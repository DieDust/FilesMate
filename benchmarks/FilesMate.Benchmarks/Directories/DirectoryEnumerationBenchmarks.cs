using BenchmarkDotNet.Attributes;

using FilesMate.Core.Directories;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.Benchmarks.Directories;

[MemoryDiagnoser]
public class DirectoryEnumerationBenchmarks
{
    private string _root = string.Empty;
    private WindowsDirectoryEnumerator _enumerator = null!;

    [Params(1_000, 10_000)]
    public int FileCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FilesMateBench", FileCount.ToString());
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        Directory.CreateDirectory(_root);
        for (var i = 0; i < FileCount; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i:D6}.txt"), "x");
        }

        _enumerator = new WindowsDirectoryEnumerator();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Benchmark]
    public async Task<int> Win32BatchedEnumeration()
    {
        var request = new DirectoryRequest(PaneId.New(), 1, _root, DirectoryReadOptions.Default);
        var count = 0;
        await foreach (var batch in _enumerator.EnumerateAsync(request, CancellationToken.None))
        {
            count += batch.Entries.Count;
        }

        return count;
    }

    [Benchmark]
    public int ManagedEnumerateFileSystemEntries()
    {
        var count = 0;
        foreach (var _ in Directory.EnumerateFileSystemEntries(_root))
        {
            count++;
        }

        return count;
    }
}
