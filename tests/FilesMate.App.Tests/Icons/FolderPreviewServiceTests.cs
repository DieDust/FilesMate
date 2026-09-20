using FilesMate.App.Icons;
using FilesMate.Core.Icons;

namespace FilesMate.App.Tests.Icons;

public sealed class FolderPreviewServiceTests
{
    private static readonly IconBitmap Cover = new(1, 1, new byte[4]);
    private static readonly string Folder = Path.GetFullPath("preview-test");

    [Fact]
    public async Task Scanning_does_not_block_the_calling_thread()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FolderPreviewService((_, _, _) => Task.FromResult<IconBitmap?>(Cover), (_, token) =>
        {
            entered.SetResult();
            release.Wait(token);
            return ["cover.png"];
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = service.GetAsync(Folder, 48, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        Assert.False(pending.IsCompleted);
        release.Set();
        Assert.Same(Cover, await pending);
    }

    [Fact]
    public async Task Shared_request_survives_one_waiter_cancelling()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IconBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = new FolderPreviewService(async (_, _, token) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        }, (_, _) => ["cover.png"]);
        using var first = new CancellationTokenSource();
        var a = service.GetAsync(Folder, 48, first.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var b = service.GetAsync(Folder, 48, CancellationToken.None);
        first.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        release.SetResult(Cover);
        Assert.Same(Cover, await b.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Last_waiter_cancels_decoder_and_releases_worker()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FolderPreviewService(async (_, _, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cancelled.TrySetResult(); }
            return null;
        }, (_, _) => ["cover.png"]);
        using var cts = new CancellationTokenSource();
        var pending = service.GetAsync(Folder, 48, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Expired_empty_result_is_replaced_and_cached()
    {
        var clock = new Clock();
        var scans = 0;
        var service = new FolderPreviewService((_, _, _) => Task.FromResult<IconBitmap?>(Cover),
            (_, _) => ++scans == 1 ? [] : ["cover.png"], clock);
        Assert.Null(await service.GetAsync(Folder, 48, default));
        Assert.Null(await service.GetAsync(Folder, 48, default));
        Assert.Equal(1, scans);
        clock.Now += TimeSpan.FromSeconds(16);
        Assert.Same(Cover, await service.GetAsync(Folder, 48, default));
        clock.Now += TimeSpan.FromSeconds(16);
        Assert.Same(Cover, await service.GetAsync(Folder, 48, default));
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task First_successful_cover_does_not_wait_for_other_candidates()
    {
        var calls = new List<string>();
        var service = new FolderPreviewService((path, _, _) =>
        {
            calls.Add(path);
            return Task.FromResult(path == "broken.png" ? null : Cover);
        }, (_, _) => ["broken.png", "cover.png", "slow.mp4"]);
        Assert.Same(Cover, await service.GetAsync(Folder, 48, default));
        Assert.Equal(["broken.png", "cover.png"], calls);
    }

    [Fact]
    public async Task Burst_is_bounded_and_queued_cancelled_folders_are_never_scanned()
    {
        var calls = 0;
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FolderPreviewService(async (_, _, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2)
                twoStarted.TrySetResult();
            await release.Task.WaitAsync(token);
            return Cover;
        }, (_, _) => ["cover.png"]);
        var a = service.GetAsync(Folder + "a", 48, default);
        var b = service.GetAsync(Folder + "b", 48, default);
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var queued = Enumerable.Range(0, 500).Select(i => service.GetAsync(Folder + i, 48, cts.Token)).ToArray();
        cts.Cancel();
        foreach (var task in queued)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        release.SetResult();
        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Clear_does_not_repopulate_cache_from_old_work()
    {
        var calls = 0;
        var service = new FolderPreviewService((_, _, _) =>
        {
            calls++;
            return Task.FromResult<IconBitmap?>(Cover);
        }, (_, _) => ["cover.png"]);
        await service.GetAsync(Folder, 48, default);
        service.ClearCache();
        await service.GetAsync(Folder, 48, default);
        Assert.Equal(2, calls);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Warm_lookup_is_immediate_including_empty_covers_and_honors_expiration(bool hasCover)
    {
        var clock = new Clock();
        var scans = 0;
        var service = new FolderPreviewService((_, _, _) => Task.FromResult<IconBitmap?>(Cover),
            (_, _) => { scans++; return hasCover ? ["cover.png"] : []; }, clock);
        Assert.False(service.TryGetCached(Folder, 48, out _));
        await service.GetAsync(Folder, 48, default);
        Assert.True(service.TryGetCached(Folder, 48, out var bitmap));
        Assert.Same(hasCover ? Cover : null, bitmap);
        Assert.Equal(1, scans);
        clock.Now += TimeSpan.FromSeconds(hasCover ? 121 : 16);
        Assert.False(service.TryGetCached(Folder, 48, out _));
        Assert.Equal(1, scans);
    }
}
