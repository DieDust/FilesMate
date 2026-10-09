using System.Runtime.CompilerServices;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveSessionTests
{
    [Fact]
    public async Task Completed_and_paused_sessions_share_one_finite_budget_until_normal_disposal()
    {
        var options = new RecursiveReadOptions
        {
            AllowContinuation = true, MaximumDepth = 8, MinimumViewStateReserveBytes = 1024,
            DirectoryOptions = DirectoryReadOptions.Default with { BatchSize = 8 }
        };
        var cost = (await ScanAsync(Tree((Root, [])), options)).Budget.EstimatedRetainedBytes;
        var pool = new RecursiveMemoryBudgetPool(cost, () => new(long.MaxValue, long.MaxValue, long.MaxValue));
        await using var first = Session(Tree((Root, [])), options with { MemoryBudgetPool = pool });
        await first.RunAsync().WaitAsync(TestTimeout);
        Assert.Equal(cost, pool.Statistics.ReservedBytes);
        Assert.Equal(1, pool.Statistics.Sessions);
        Assert.True(first.TryReserveViewState(300));
        Assert.False(first.TryReserveViewState(100));
        Assert.Equal(cost, pool.Statistics.ReservedBytes);
        var paused = Signal();
        var enumerator = Tree((Root, []));
        await using var second = Session(enumerator, options with { MemoryBudgetPool = pool });
        second.Changed += change => { if (change.State == RecursiveSessionState.Paused) paused.TrySetResult(); };
        var run = second.RunAsync();
        await paused.Task.WaitAsync(TestTimeout);
        Assert.Empty(enumerator.Requests);
        Assert.True(second.CurrentSnapshot.Budget.SharedPressure);
        Assert.False(second.Continue());
        await first.DisposeAsync();
        Assert.Equal(0, pool.Statistics.ReservedBytes);
        Assert.True(second.Continue());
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed, second.State);
        Assert.Equal(cost, pool.Statistics.ReservedBytes);
        await second.DisposeAsync();
        Assert.Equal(0, pool.Statistics.ReservedBytes);
        Assert.Equal(0, pool.Statistics.Sessions);
        await second.DisposeAsync();
        Assert.Equal(0, pool.Statistics.ReservedBytes);
    }

    [Fact]
    public async Task Paused_scan_preserves_budgeted_view_history_room_and_its_exact_cursor()
    {
        var entries = Enumerable.Range(1, 8).Select(i => File($"item-{i}.txt")).ToArray();
        var options = new RecursiveReadOptions
        {
            AllowContinuation = true,
            MaximumDepth = 8,
            MinimumViewStateReserveBytes = 1024,
            DirectoryOptions = DirectoryReadOptions.Default with { BatchSize = 8 }
        };
        var firstFour = await ScanAsync(Tree((Root, entries[..4])), options);
        var allowance = firstFour.Budget.EstimatedRetainedBytes;
        var pool = new RecursiveMemoryBudgetPool(2 * allowance,
            () => new(long.MaxValue, long.MaxValue, long.MaxValue));
        var paused = new TaskCompletionSource<RecursiveSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = Tree((Root, entries));
        await using var session = Session(enumerator, options with
        {
            InitialRetainedByteAllowance = allowance,
            MaximumRetainedByteAllowance = 2 * allowance,
            RetainedByteStep = allowance,
            MemoryBudgetPool = pool
        });
        session.Changed += change => { if (change.State == RecursiveSessionState.Paused) paused.TrySetResult(change.Snapshot); };
        var run = session.RunAsync();
        var snapshot = await paused.Task.WaitAsync(TestTimeout);
        Assert.Equal(4, snapshot.Count);
        Assert.Equal(allowance, snapshot.Budget.EstimatedRetainedBytes);
        var reserved = pool.Statistics.ReservedBytes;
        Assert.True(session.TryReserveViewState(300));
        Assert.Equal(reserved, pool.Statistics.ReservedBytes);
        Assert.Same(snapshot, session.CurrentSnapshot);
        Assert.False(run.IsCompleted);
        Assert.False(session.TryReserveViewState(100));
        Assert.Equal(reserved, pool.Statistics.ReservedBytes);
        Assert.True(session.Continue());
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed, session.State);
        Assert.Equal(entries.Select(entry => entry.Name), session.CurrentSnapshot.Entries.Select(entry => entry.Name));
        Assert.Single(enumerator.Requests);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2049)]
    public void View_history_headroom_must_fit_initial_admission(long reserve)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Session(Tree((Root, [])), new()
        {
            InitialRetainedByteAllowance = 2048,
            MinimumViewStateReserveBytes = reserve
        }));
    }

    [Fact]
    public async Task Empty_root_completes_once_and_run_is_idempotent()
    {
        var enumerator = Tree((Root, []));
        await using var session = Session(enumerator);
        var changes = new List<RecursiveSessionChange>();
        session.Changed += changes.Add;
        var run = session.RunAsync();
        Assert.Same(run, session.RunAsync());
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed, session.State);
        Assert.Empty(session.CurrentSnapshot.Entries);
        Assert.Single(enumerator.Requests);
        var final = Assert.Single(changes, change => change.State == RecursiveSessionState.Completed);
        Assert.Equal(session.SessionId, final.SessionId);
        Assert.Equal(session.Generation, final.Generation);
        Assert.Equal(final.Count, final.EntryCount);
        Assert.False(final.LimitReached);
        Assert.Null(final.Error);
    }

    [Fact]
    public async Task Foreign_generation_pane_and_path_batches_including_finals_are_ignored()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => Batches(
                DirectoryBatch.Create(request.PaneId, request.Generation - 1, request.Path, [File("old.txt")], true, null),
                DirectoryBatch.Create(PaneId.New(), request.Generation, request.Path, [File("pane.txt")], true, null),
                DirectoryBatch.Create(request.PaneId, request.Generation, request.Path + "-other", [File("path.txt")], true, null),
                Batch(request, [File("accepted.txt")]))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.Equal("accepted.txt", Assert.Single(snapshot.Entries).Name);
    }

    [Fact]
    public async Task Foreign_final_does_not_complete_an_unfinished_valid_stream()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => Batches(Batch(request, [File("kept.txt")], final: false),
                DirectoryBatch.Create(request.PaneId, request.Generation - 1, request.Path, [], true, null))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Failed, snapshot.State);
        Assert.Equal("kept.txt", Assert.Single(snapshot.Entries).Name);
        Assert.Equal(DirectoryReadErrorKind.PartialEnumeration, snapshot.Error?.Kind);
    }

    [Fact]
    public async Task Normalized_windows_case_and_extended_prefix_batch_identity_is_accepted()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => Batches(DirectoryBatch.Create(request.PaneId, request.Generation,
                @"\\?\d:\RECURSIVE-FIXTURE\", [File("kept.txt")], true, null))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.Single(snapshot.Entries);
    }

    [Fact]
    public async Task Enumeration_uses_session_identity_and_disables_eager_recursive_metadata_work()
    {
        var options = new RecursiveReadOptions
        {
            DirectoryOptions = new()
            {
                IncludeHidden = false, IncludeSystem = false, IncludeCreatedTime = false,
                IncludeFolderItemCounts = true, IncludeShortcutTargets = true, IncludeHardLinkCounts = true,
                BatchSize = 3, BatchDeadline = TimeSpan.FromMilliseconds(25)
            }
        };
        var enumerator = Tree((Root, [Directory("A")]), (Root + @"\A", [File("a.txt")]));
        await using var session = Session(enumerator, options);
        await session.RunAsync().WaitAsync(TestTimeout);
        Assert.Equal(2, enumerator.Requests.Count);
        foreach (var request in enumerator.Requests)
        {
            Assert.Equal(session.Generation, request.Generation);
            Assert.Equal(session.PaneId, request.PaneId);
            Assert.False(request.Options.IncludeHidden);
            Assert.False(request.Options.IncludeSystem);
            Assert.False(request.Options.IncludeCreatedTime);
            Assert.False(request.Options.IncludeFolderItemCounts);
            Assert.False(request.Options.IncludeShortcutTargets);
            Assert.False(request.Options.IncludeHardLinkCounts);
            Assert.Equal(3, request.Options.BatchSize);
            Assert.Equal(TimeSpan.FromMilliseconds(25), request.Options.BatchDeadline);
        }
    }

    [Fact]
    public async Task Published_nodes_and_index_remain_immutable_across_append_and_traversal_changes()
    {
        var waiting = Signal();
        var release = Signal();
        var published = new TaskCompletionSource<RecursiveSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstEntries = new[] { Directory("A") }.Concat(Enumerable.Range(1, 299).Select(i => File($"first-{i}.txt"))).ToArray();
        var secondEntries = Enumerable.Range(1, 300).Select(i => File($"second-{i}.txt")).ToArray();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (request.Path == Root)
            {
                await Task.Delay(40, token);
                yield return Batch(request, firstEntries, final: false);
                waiting.TrySetResult();
                await release.Task.WaitAsync(token);
                yield return Batch(request, secondEntries);
            }
            else yield return Batch(request, [File("leaf.txt")]);
        }
        var enumerator = new ScriptedEnumerator { Read = Read };
        await using var session = Session(enumerator, new() { ProgressInterval = TimeSpan.FromMilliseconds(16) });
        session.Changed += change => { if (change.Count == 300) published.TrySetResult(change.Snapshot); };
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            var earlier = await published.Task.WaitAsync(TestTimeout);
            var projection = Project(earlier);
            var earlierPaths = Paths(projection);
            var earlierRows = earlier.Entries.ToArray();
            Assert.Equal(RecursiveTraversalState.Pending, earlier.GetNode(1).TraversalState);
            release.TrySetResult();
            await run.WaitAsync(TestTimeout);
            var final = session.CurrentSnapshot;
            Assert.Equal(601, final.Count);
            Assert.Equal(300, earlier.Count);
            Assert.Equal(earlierRows, earlier.Entries.ToArray());
            Assert.Equal(earlierPaths, Paths(projection));
            Assert.Equal(RecursiveTraversalState.Pending, earlier.GetNode(1).TraversalState);
            Assert.Equal(RecursiveTraversalState.Completed, final.GetNode(1).TraversalState);
            Assert.True(projection.Index.CanPublish(projection.Store));
            Assert.False(projection.Index.AsFinal().CanPublish(projection.Store));
            Assert.Equal("A", earlier.ResolveRelativePath(1));
            Assert.Equal(Root + @"\A\leaf.txt", final.ResolvePath(Id(final, @"A\leaf.txt")));
            Assert.True(final.Revision > earlier.Revision);
            Assert.Throws<ArgumentOutOfRangeException>(() => earlier.GetNode(301));
            var finalProjection = Project(final);
            Assert.True(finalProjection.Index.CanPublish(finalProjection.Store));
            Assert.Same(finalProjection.Index, finalProjection.Index.AsFinal());
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Cancel_freezes_snapshot_and_suppresses_late_batches_and_notifications()
    {
        var waiting = Signal();
        var release = Signal();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Delay(40, token);
            yield return Batch(request, [File("first.txt")], final: false);
            waiting.TrySetResult();
            // Deliberately ignore cancellation to model a provider completing already-started I/O late.
            await release.Task;
            yield return Batch(request, [File("late.txt")]);
        }
        var enumerator = new ScriptedEnumerator { Read = Read };
        await using var session = Session(enumerator, new() { ProgressInterval = TimeSpan.FromMilliseconds(16) });
        var changes = new List<RecursiveSessionChange>();
        session.Changed += changes.Add;
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            var before = session.CurrentSnapshot;
            Assert.Equal(1, before.Count);
            session.Cancel();
            var afterCancel = session.CurrentSnapshot;
            var changeCount = changes.Count;
            Assert.Equal(RecursiveSessionState.Cancelled, afterCancel.State);
            Assert.True(afterCancel.Revision > before.Revision);
            var projection = Project(afterCancel);
            Assert.True(projection.Index.CanPublish(projection.Store));
            release.TrySetResult();
            await run.WaitAsync(TestTimeout);
            session.Cancel();
            Assert.Same(afterCancel, session.CurrentSnapshot);
            Assert.Equal(changeCount, changes.Count);
            Assert.Equal(1, projection.Store.Count);
            Assert.Equal(new[] { "first.txt" }, Paths(projection));
            Assert.Equal(1, enumerator.DisposeCount);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Cancelled_snapshot_with_unpublished_store_append_remains_coherent_and_publishable()
    {
        var waiting = Signal();
        var release = Signal();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            yield return Batch(request, [File("published.txt")], final: false);
            yield return Batch(request, [File("unpublished.txt")], final: false);
            waiting.TrySetResult();
            await release.Task;
            yield return Batch(request, [File("late.txt")]);
        }
        var enumerator = new ScriptedEnumerator { Read = Read };
        await using var session = Session(enumerator, new() { ProgressInterval = TimeSpan.FromSeconds(10) });
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            Assert.Equal(1, session.CurrentSnapshot.Count);
            session.Cancel();
            var cancelled = session.CurrentSnapshot;
            var projection = Project(cancelled);
            Assert.Equal(RecursiveSessionState.Cancelled, cancelled.State);
            Assert.Single(projection.Index);
            Assert.Equal(new[] { "published.txt" }, Paths(projection));
            Assert.Equal(2, projection.Store.Count);
            Assert.True(projection.Index.CanPublish(projection.Store));
            release.TrySetResult();
            await run.WaitAsync(TestTimeout);
            Assert.Same(cancelled, session.CurrentSnapshot);
            Assert.Equal(2, projection.Store.Count);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Pre_cancelled_start_and_cancel_before_start_do_not_probe_or_enumerate()
    {
        foreach (var cancelExternally in new[] { false, true })
        {
            var probe = new Probe();
            var enumerator = Tree((Root, [File("never.txt")]));
            await using var session = Session(enumerator, probe: probe);
            if (!cancelExternally) session.Cancel();
            await session.RunAsync(new CancellationToken(cancelExternally)).WaitAsync(TestTimeout);
            Assert.Equal(RecursiveSessionState.Cancelled, session.State);
            Assert.Empty(probe.Paths);
            Assert.Empty(enumerator.Requests);
        }
    }

    [Fact]
    public async Task External_cancellation_stops_running_provider_and_dispose_repeats_are_safe()
    {
        var started = Signal();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            started.TrySetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            yield return Batch(request, []);
        }
        var enumerator = new ScriptedEnumerator { Read = Read };
        using var cancellation = new CancellationTokenSource();
        var session = Session(enumerator);
        var run = session.RunAsync(cancellation.Token);
        await started.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await run.WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Cancelled, session.State);
        session.Dispose();
        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        session.Dispose();
        session.Cancel();
        Assert.Equal(1, enumerator.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => { _ = session.RunAsync(); });
    }

    [Fact]
    public async Task Dispose_before_start_is_idempotent_and_no_events_follow_disposal()
    {
        var enumerator = Tree((Root, []));
        var session = Session(enumerator);
        var changes = 0;
        session.Changed += _ => changes++;
        session.Dispose();
        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => { _ = session.RunAsync(); });
        Assert.Empty(enumerator.Requests);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Completed_snapshot_is_not_relabelled_by_later_cancellation_or_disposal()
    {
        var session = Session(Tree((Root, [File("kept.txt")])));
        await session.RunAsync().WaitAsync(TestTimeout);
        var final = session.CurrentSnapshot;
        session.Cancel();
        await session.DisposeAsync();
        Assert.Same(final, session.CurrentSnapshot);
        Assert.Equal(RecursiveSessionState.Completed, final.State);
    }
    [Fact]
    public async Task Throwing_notification_observer_does_not_abort_scan_or_other_observers()
    {
        var enumerator = Tree((Root, [File("kept.txt")]));
        await using var session = Session(enumerator);
        var error = new InvalidOperationException("fixture observer failure");
        var observed = new List<RecursiveSessionChange>();
        session.Changed += _ => throw error;
        session.Changed += observed.Add;
        await session.RunAsync().WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed, session.State);
        Assert.Same(error, session.NotificationError);
        Assert.Null(session.CurrentSnapshot.Error);
        Assert.Contains(observed, change => change.State == RecursiveSessionState.Completed);
        Assert.Single(session.CurrentSnapshot.Entries);
    }

    [Fact]
    public async Task Observer_cancellation_suppresses_subsequent_observers_and_late_provider_rows()
    {
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Delay(40, token);
            yield return Batch(request, [File("first.txt")], final: false);
            yield return Batch(request, [File("late.txt")]);
        }
        var enumerator = new ScriptedEnumerator { Read = Read };
        await using var session = Session(enumerator, new() { ProgressInterval = TimeSpan.FromMilliseconds(16) });
        var laterCalls = 0;
        session.Changed += _ => session.Cancel();
        session.Changed += _ => laterCalls++;
        await session.RunAsync().WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Cancelled, session.State);
        Assert.Equal(0, laterCalls);
        Assert.Equal("first.txt", Assert.Single(session.CurrentSnapshot.Entries).Name);
        Assert.Equal(1, enumerator.DisposeCount);
    }

    [Fact]
    public async Task Dispose_waits_for_provider_cleanup_but_stops_publication_synchronously()
    {
        var waiting = Signal();
        var release = Signal();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            yield return Batch(request, [File("first.txt")], final: false);
            waiting.TrySetResult();
            await release.Task;
            yield return Batch(request, [File("late.txt")]);
        }
        var enumerator = new ScriptedEnumerator { Read = Read };
        var session = Session(enumerator);
        var changes = 0;
        session.Changed += _ => changes++;
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            var disposal = session.DisposeAsync().AsTask();
            var cancelled = session.CurrentSnapshot;
            var count = changes;
            Assert.False(disposal.IsCompleted);
            Assert.Equal(RecursiveSessionState.Cancelled, cancelled.State);
            release.TrySetResult();
            await disposal.WaitAsync(TestTimeout);
            await run.WaitAsync(TestTimeout);
            Assert.Same(cancelled, session.CurrentSnapshot);
            Assert.Equal(count, changes);
            Assert.Equal(1, enumerator.DisposeCount);
            await session.DisposeAsync();
        }
        finally { release.TrySetResult(); await session.DisposeAsync(); }
    }

}
