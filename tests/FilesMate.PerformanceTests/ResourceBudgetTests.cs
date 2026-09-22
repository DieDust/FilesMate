using System.Diagnostics;
using System.Text.Json;
using FilesMate.App.Navigation;
using FilesMate.Core.Directories;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;
using Xunit.Abstractions;

namespace FilesMate.PerformanceTests;

// These enforce bounded work and release, not hardware-specific frame-time gates.
public sealed class ResourceBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void Repeated_metadata_burst_keeps_work_and_allocations_bounded()
    {
        var pane = PaneId.New();
        var notices = Enumerable.Range(0, 8).Select(i =>
            new DirectoryWatchNotification(pane, 1, DirectoryWatchKind.Modified, "file-" + i)).ToArray();
        for (var round = 0; round < 6; round++)
        {
            var buffer = new DirectoryWatchBuffer(1024);
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 100_000; i++) buffer.Enqueue(notices[i % notices.Length]);
            var milliseconds = watch.Elapsed.TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var reads = 0;
            while (buffer.TryDequeue(out var notice))
            {
                Assert.Equal(DirectoryWatchKind.Modified, notice.Kind);
                reads++;
            }
            Assert.Equal(8, reads);
            Assert.InRange(bytes, 0, 64 * 1024);
            if (round > 0) Report(new { Scenario = "MetadataBurst", Round = round, Events = 100_000,
                PendingMetadataReads = reads, AllocatedBytes = bytes, Milliseconds = milliseconds });
        }
    }

    [Fact]
    public async Task Repeated_early_enumerator_disposal_releases_producers()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-resource-probe-").FullName;
        using var emergency = new CancellationTokenSource();
        try
        {
            for (var i = 0; i < 40; i++) File.WriteAllText(Path.Combine(root, "file-" + i), "fixture");
            for (var round = 0; round < 6; round++)
            {
                var watch = Stopwatch.StartNew();
                for (var cycle = 0; cycle < 20; cycle++)
                {
                    var request = new DirectoryRequest(PaneId.New(), cycle, root, new DirectoryReadOptions { BatchSize = 1 });
                    var reader = new WindowsDirectoryEnumerator().EnumerateAsync(request, emergency.Token).GetAsyncEnumerator();
                    Assert.True(await reader.MoveNextAsync());
                    var dispose = reader.DisposeAsync().AsTask();
                    try { await dispose.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch { emergency.Cancel(); await dispose.WaitAsync(TimeSpan.FromSeconds(5)); throw; }
                }
                Assert.False(emergency.IsCancellationRequested);
                if (round > 0) Report(new { Scenario = "EarlyDirectoryDisposal", Round = round,
                    Cycles = 20, Milliseconds = watch.Elapsed.TotalMilliseconds });
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private void Report<T>(T sample) => output.WriteLine("PERF_SAMPLE " + JsonSerializer.Serialize(sample));
}
