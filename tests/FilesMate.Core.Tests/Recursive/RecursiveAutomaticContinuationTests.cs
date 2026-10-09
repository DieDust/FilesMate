using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveAutomaticContinuationTests
{
    private static RecursiveReadOptions Automatic => new()
    {
        AllowContinuation = true, AutomaticContinuation = true,
        DirectoryOptions = DirectoryReadOptions.Default with { BatchSize = 1024 }
    };

    [Fact]
    public void Automatic_continuation_requires_retaining_continuation_state()
    {
        Assert.Throws<ArgumentException>(() => Session(Tree((Root, [])), new() { AutomaticContinuation = true }));
    }

    [Fact]
    public async Task Automatically_reads_all_205005_entries_across_byte_and_node_steps()
    {
        const int count = 205005;
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => Batches(Enumerable.Range(0, count).Chunk(1024)
                .Select(ids => Batch(request, ids.Select(i => File($"entry-{i:D6}.txt")).ToArray(), ids[^1] == count - 1)).ToArray())
        };
        await using var session = Session(enumerator, Automatic);
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var snapshot = session.CurrentSnapshot;
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.Equal(count, snapshot.Count);
        Assert.Equal(count, snapshot.Entries.Select(e => e.Name).Distinct().Count());
        Assert.Equal("entry-000000.txt", snapshot.GetEntry(1).Name);
        Assert.Equal("entry-205004.txt", snapshot.GetEntry(count).Name);
        Assert.False(snapshot.LimitReached);
        Assert.Single(enumerator.Requests);
    }

    [Fact]
    public async Task Automatic_steps_preserve_current_batch_and_queued_subtree_order()
    {
        var child = Path.Combine(Root, "folder");
        var enumerator = Tree((Root, [Directory("folder"), File("a.txt"), File("b.txt")]),
            (child, [File("c.txt"), File("d.txt"), File("e.txt")]));
        await using var session = Session(enumerator, Automatic with { MaximumNodes = 2 });
        await session.RunAsync().WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed, session.State);
        Assert.Equal(new[] { "folder", "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" },
            session.CurrentSnapshot.Entries.Select(e => e.Name));
        Assert.Equal(new[] { Root, child }, enumerator.Requests.Select(r => r.Path));
    }

    [Fact]
    public async Task Automatically_resumes_shared_pressure_after_other_session_releases()
    {
        var options = Automatic with { MaximumDepth = 8 };
        var cost = (await ScanAsync(Tree((Root, [])), options)).Budget.EstimatedRetainedBytes;
        var pool = new RecursiveMemoryBudgetPool(cost, () => new(long.MaxValue, long.MaxValue, long.MaxValue));
        await using var first = Session(Tree((Root, [])), options with { MemoryBudgetPool = pool });
        await first.RunAsync().WaitAsync(TestTimeout);
        var paused = Signal();
        var enumerator = Tree((Root, []));
        await using var second = Session(enumerator, options with { MemoryBudgetPool = pool });
        second.Changed += c => { if(c.State == RecursiveSessionState.Paused) paused.TrySetResult(); };
        var run = second.RunAsync();
        await paused.Task.WaitAsync(TestTimeout);
        Assert.False(run.IsCompleted);
        Assert.Empty(enumerator.Requests);
        await first.DisposeAsync();
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed, second.State);
        Assert.Single(enumerator.Requests);
    }

    [Fact]
    public async Task Cancel_wakes_automatic_memory_wait_without_provider_work()
    {
        var pool = new RecursiveMemoryBudgetPool(1024 * 1024, () => default);
        var paused = Signal();
        var enumerator = Tree((Root, []));
        await using var session = Session(enumerator, Automatic with { MemoryBudgetPool = pool });
        session.Changed += c => { if(c.State == RecursiveSessionState.Paused) paused.TrySetResult(); };
        var run = session.RunAsync();
        await paused.Task.WaitAsync(TestTimeout);
        session.Cancel();
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Cancelled, session.State);
        Assert.Empty(enumerator.Requests);
    }

    [Fact]
    public async Task Finite_ceiling_remains_honestly_paused_and_cancelable()
    {
        var cost = (await ScanAsync(Tree((Root, [File("a.txt"), File("b.txt")])), Automatic)).Budget.EstimatedRetainedBytes;
        var paused = Signal();
        await using var session = Session(Tree((Root, [File("a.txt"), File("b.txt"), File("c.txt")])), Automatic with
        {
            InitialRetainedByteAllowance = cost, MaximumRetainedByteAllowance = cost
        });
        session.Changed += c => { if(c.State == RecursiveSessionState.Paused) paused.TrySetResult(); };
        var run = session.RunAsync();
        await paused.Task.WaitAsync(TestTimeout);
        Assert.Equal(2, session.CurrentSnapshot.Count);
        Assert.False(session.CurrentSnapshot.Budget.CanContinue);
        Assert.False(run.IsCompleted);
        session.Cancel();
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Cancelled, session.State);
    }
}
