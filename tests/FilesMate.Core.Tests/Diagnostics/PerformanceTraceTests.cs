using System.Diagnostics;
using System.Text.Json;
using FilesMate.Core.Diagnostics;

namespace FilesMate.Core.Tests.Diagnostics;

public sealed class PerformanceTraceTests
{
    [Fact]
    public async Task Concurrent_producers_preserve_packets_through_multiple_ring_wraps()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-pr00-wrap-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            await using (var writer = new PerformanceTraceWriter(path, Guid.NewGuid(), capacity: 16, maxBytes: 1024 * 1024))
            {
                await Task.WhenAll(Enumerable.Range(0, 4).Select(async producer =>
                {
                    for (var i = 0; i < 32; i++)
                    {
                        var id = producer * 32 + i;
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        while (!writer.Record(PerformanceStage.EnumerationBatch, generation: producer,
                            operation: id, count: id * 10)) await Task.Delay(5, deadline.Token);
                    }
                }));
            }
            var packets = File.ReadLines(path).Select(line => JsonDocument.Parse(line)).ToArray();
            try
            {
                var events = packets.Where(p => p.RootElement.GetProperty("Type").GetString() == "event").ToArray();
                Assert.Equal(128, events.Length);
                Assert.Equal(128, events.Select(p => p.RootElement.GetProperty("Operation").GetInt64()).Distinct().Count());
                Assert.All(events, packet =>
                {
                    var e = packet.RootElement;
                    var id = e.GetProperty("Operation").GetInt64();
                    Assert.Equal(id * 10, e.GetProperty("Count").GetInt64());
                    Assert.Equal(id / 32, e.GetProperty("Generation").GetInt64());
                });
            }
            finally { foreach (var packet in packets) packet.Dispose(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Correlation_clock_and_cancelled_ending_survive_export_without_user_strings()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-pr00-test-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var run = Guid.NewGuid();
        var pane = Guid.NewGuid();
        var before = Stopwatch.GetTimestamp();
        try
        {
            await using (var writer = new PerformanceTraceWriter(path, run))
            {
                Assert.True(writer.Record(PerformanceStage.EnumerationBatch, pane, 7, 4, 128, PerformanceOutcome.Partial));
                Assert.True(writer.Record(PerformanceStage.EnumerationEnd, pane, 7, 4, 128, PerformanceOutcome.Cancelled));
            }
            var lines = File.ReadAllLines(path);
            using var header = JsonDocument.Parse(lines[0]);
            Assert.Equal(Stopwatch.Frequency, header.RootElement.GetProperty("Frequency").GetInt64());
            using var first = JsonDocument.Parse(lines[1]);
            using var last = JsonDocument.Parse(lines[2]);
            Assert.Equal(run, first.RootElement.GetProperty("RunId").GetGuid());
            Assert.Equal(pane, last.RootElement.GetProperty("Pane").GetGuid());
            Assert.Equal(7, last.RootElement.GetProperty("Generation").GetInt64());
            Assert.Equal(4, last.RootElement.GetProperty("Operation").GetInt64());
            Assert.Equal("Cancelled", last.RootElement.GetProperty("Outcome").GetString());
            Assert.InRange(first.RootElement.GetProperty("Timestamp").GetInt64(), before, Stopwatch.GetTimestamp());
            Assert.True(last.RootElement.GetProperty("Timestamp").GetInt64() >= first.RootElement.GetProperty("Timestamp").GetInt64());
            Assert.DoesNotContain(path, string.Join('\n', lines));
            Assert.All(new[] { "Path", "Name", "Query", "Input", "Clipboard" }, key =>
                Assert.False(first.RootElement.TryGetProperty(key, out _)));
            using var summary = JsonDocument.Parse(lines[^1]);
            Assert.True(summary.RootElement.GetProperty("Complete").GetBoolean());
            Assert.Equal(2, summary.RootElement.GetProperty("Written").GetInt64());
            Assert.True(summary.RootElement.GetProperty("AllocatedBytesSinceTraceStart").GetInt64() >= 0);
            Assert.True(summary.RootElement.GetProperty("Gen0Collections").GetInt32() >= 0);
            Assert.True(summary.RootElement.GetProperty("Gen1Collections").GetInt32() >= 0);
            Assert.True(summary.RootElement.GetProperty("Gen2Collections").GetInt32() >= 0);
            Assert.True(summary.RootElement.GetProperty("GcPauseMilliseconds").GetDouble() >= 0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Pressure_and_byte_limit_remain_bounded_and_incomplete_is_visible()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-pr00-bounds-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var writer = new PerformanceTraceWriter(path, Guid.NewGuid(), capacity: 8, maxBytes: 2048);
            Parallel.For(0, 100000, i => writer.Record(PerformanceStage.BuildStart, operation: i));
            await writer.DisposeAsync();
            Assert.False(writer.Record(PerformanceStage.BuildEnd));
            Assert.InRange(new FileInfo(path).Length, 1, 2048);
            using var summary = JsonDocument.Parse(File.ReadLines(path).Last());
            Assert.Equal("summary", summary.RootElement.GetProperty("Type").GetString());
            Assert.False(summary.RootElement.GetProperty("Complete").GetBoolean());
            Assert.True(summary.RootElement.GetProperty("Dropped").GetInt64() > 0);
            Assert.True(summary.RootElement.GetProperty("Truncated").GetBoolean());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Writer_failure_does_not_replace_existing_data_or_throw_into_product()
    {
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-pr00-existing-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, "owned previous run");
            await using (var writer = new PerformanceTraceWriter(path, Guid.NewGuid()))
                writer.Record(PerformanceStage.SearchEnd, outcome: PerformanceOutcome.Failed);
            Assert.Equal("owned previous run", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Dispatcher_probe_has_one_in_flight_and_stops_with_pending_post()
    {
        var posts = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var probe = new DispatcherDelayProbe(action => { posts.Enqueue(action); return true; });
        var deadline = Stopwatch.StartNew();
        while (posts.IsEmpty && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
        Assert.Single(posts);
        await Task.Delay(600);
        Assert.Single(posts);
        probe.Dispose();
        await probe.WhenStopped.WaitAsync(TimeSpan.FromSeconds(2));
        posts.TryDequeue(out var pending);
        pending!();
        Assert.Empty(posts);
    }
}
