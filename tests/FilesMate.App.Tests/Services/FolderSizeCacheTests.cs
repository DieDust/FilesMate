using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class FolderSizeCacheTests
{
    [Fact]
    public async Task Shared_walk_survives_first_subscriber_canceling()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var service = new FolderSizeService((_, token, progress) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5), token));
            progress?.Invoke(42);
            return 42;
        });
        using var firstToken = new CancellationTokenSource();
        var first = service.GetAsync(@"C:\shared", firstToken.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ulong reported = 0;
        var others = Enumerable.Range(0, 24).Select(_ => service.GetAsync(@"C:\SHARED\", progress: n => reported = n)).ToArray();
        try
        {
            firstToken.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        finally { release.Set(); }
        Assert.All(await Task.WhenAll(others), size => Assert.Equal(42UL, size));
        Assert.Equal(42UL, reported);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Only_two_walks_run_and_canceled_queue_never_starts()
    {
        using var canceled = new CancellationTokenSource();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bothStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var stopped = 0;
        var service = new FolderSizeService((_, token, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2) bothStarted.TrySetResult();
            try
            {
                Assert.True(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)));
                token.ThrowIfCancellationRequested();
                return 0;
            }
            finally { if (Interlocked.Increment(ref stopped) == 2) bothStopped.TrySetResult(); }
        });
        var active = new[] { service.GetAsync(@"C:\first", canceled.Token), service.GetAsync(@"C:\second", canceled.Token) };
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var queuedToken = new CancellationTokenSource();
        var queued = Enumerable.Range(0, 100).Select(i => service.GetAsync($@"C:\queued{i}", queuedToken.Token)).ToArray();
        queuedToken.Cancel();
        foreach (var task in queued) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        canceled.Cancel();
        foreach (var task in active) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        await bothStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.False(service.TryGet(@"C:\first", out _));
    }

    [Fact]
    public async Task Cache_is_bounded_expires_and_invalidates_only_related_paths()
    {
        var time = new TestTime();
        var service = new FolderSizeService((_, _, _) => 12, time, capacity: 3);
        await service.GetAsync(@"C:\parent");
        await service.GetAsync(@"C:\parent\child");
        await service.GetAsync(@"C:\parent-other");
        service.Invalidate(@"C:\parent\child");
        Assert.False(service.TryGet(@"C:\parent", out _));
        Assert.False(service.TryGet(@"C:\parent\child", out _));
        Assert.True(service.TryGet(@"C:\parent-other", out _));
        await service.GetAsync(@"C:\two");
        await service.GetAsync(@"C:\three");
        await service.GetAsync(@"C:\four");
        Assert.False(service.TryGet(@"C:\parent-other", out _));
        time.Now = time.Now.AddMinutes(2);
        Assert.False(service.TryGet(@"C:\four", out _));
    }

    [Fact]
    public async Task Invalidated_walk_cannot_overwrite_newer_results()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var service = new FolderSizeService((_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) != 1) return 99;
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return 1;
        });
        var old = service.GetAsync(@"C:\folder");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Invalidate(@"C:\folder");
        try { Assert.Equal(99UL, await service.GetAsync(@"C:\folder")); }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        Assert.True(service.TryGet(@"C:\folder", out var size));
        Assert.Equal(99UL, size);
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Child_change_preserves_sibling_values_and_last_known_size_until_replacement_finishes()
    {
        var calls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var service = new FolderSizeService((path, _, _) =>
        { calls[path] = calls.GetValueOrDefault(path) + 1; return (ulong)(calls[path] * 100); });
        await service.GetAsync(@"C:\root");
        await service.GetAsync(@"C:\root\changed");
        await service.GetAsync(@"C:\root\changed\nested");
        await service.GetAsync(@"C:\root\sibling");
        service.MarkChanged(@"C:\root\changed\new.txt");
        Assert.False(service.TryGet(@"C:\root", out _));
        Assert.False(service.TryGet(@"C:\root\changed", out _));
        Assert.True(service.TryGetLastKnown(@"C:\root\changed", out var previous));
        Assert.Equal(100UL, previous);
        Assert.True(service.TryGet(@"C:\root\changed\nested", out _));
        Assert.True(service.TryGet(@"C:\root\sibling", out _));
        Assert.Equal(100UL, await service.GetAsync(@"C:\root\sibling", useUnchanged: true));
        Assert.Equal(200UL, await service.GetAsync(@"C:\root\changed", useUnchanged: true));
        Assert.Equal(1, calls[@"C:\root\sibling"]);
    }

    [Fact]
    public async Task Expired_unchanged_siblings_can_be_reused_but_dirty_folders_are_measured_again()
    {
        var time = new TestTime();
        var calls = 0;
        var service = new FolderSizeService((_, _, _) => (ulong)Interlocked.Increment(ref calls), time);
        await service.GetAsync(@"C:\root\first");
        await service.GetAsync(@"C:\root\second");
        time.Now = time.Now.AddMinutes(2);
        Assert.False(service.TryGet(@"C:\root\first", out _));
        Assert.True(service.TryGetUnchanged(@"C:\root\first", out _));
        Assert.Equal(1UL, await service.GetAsync(@"C:\root\first", useUnchanged: true));
        service.MarkChanged(@"C:\root\second");
        Assert.False(service.TryGetUnchanged(@"C:\root\second", out _));
        Assert.Equal(3UL, await service.GetAsync(@"C:\root\second", useUnchanged: true));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Replacing_existing_cache_key_at_capacity_does_not_evict_other_folder()
    {
        var time = new TestTime();
        var service = new FolderSizeService((_, _, _) => 42, time, 2);
        await service.GetAsync(@"C:\first");
        await service.GetAsync(@"C:\second");
        service.MarkChanged(@"C:\first");
        await service.GetAsync(@"C:\first");
        Assert.True(service.TryGet(@"C:\second", out _));
    }
}
