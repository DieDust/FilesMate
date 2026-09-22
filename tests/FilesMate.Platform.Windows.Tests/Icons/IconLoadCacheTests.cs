using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;

namespace FilesMate.Platform.Windows.Tests.Icons;

public sealed class IconLoadCacheTests
{
    private static IconBitmap Bitmap() => new(1, 1, new byte[4]);
    private static TaskCompletionSource<IconBitmap?> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Canceling_either_waiter_does_not_cancel_the_other(bool cancelFirst)
    {
        var cache = new IconLoadCache(1024, 2);
        var native = Pending();
        var calls = 0;
        CancellationToken shared = default;
        Task<IconBitmap?> Load(CancellationToken token)
        {
            calls++;
            shared = token;
            return native.Task;
        }
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = cache.GetAsync("same", Load, firstCancellation.Token);
        var second = cache.GetAsync("same", Load, secondCancellation.Token);
        (cancelFirst ? firstCancellation : secondCancellation).Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            (cancelFirst ? first : second).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(shared.IsCancellationRequested);
        Assert.Equal(1, calls);
        var bitmap = Bitmap();
        native.SetResult(bitmap);
        Assert.Same(bitmap, await (cancelFirst ? second : first));
        Assert.Same(bitmap, cache.TryGetCached("same"));
    }

    [Fact]
    public async Task Clearing_cache_keeps_visible_waiters_but_prevents_old_work_refilling_it()
    {
        var cache = new IconLoadCache(1024, 2);
        var native = Pending();
        CancellationToken shared = default;
        var request = cache.GetAsync("same", token => { shared = token; return native.Task; }, default);
        cache.Clear();
        Assert.False(shared.IsCancellationRequested);
        var bitmap = Bitmap();
        native.SetResult(bitmap);
        Assert.Same(bitmap, await request);
        Assert.Equal(0, cache.CacheBytes);
        Assert.Null(cache.TryGetCached("same"));
        Assert.Same(bitmap, await cache.GetAsync("same", _ => Task.FromResult<IconBitmap?>(bitmap), default));
        Assert.Equal(4, cache.CacheBytes);
    }

    [Fact]
    public async Task Metadata_work_started_before_clear_cannot_refill_the_cache()
    {
        var cache = new IconLoadCache(1024, 2);
        var generation = cache.Generation;
        cache.Clear();
        var result = await cache.GetAsync("late metadata", _ => Task.FromResult<IconBitmap?>(Bitmap()), default, generation);
        Assert.NotNull(result);
        Assert.Equal(0, cache.CacheBytes);
    }

    [Fact]
    public async Task Last_waiter_cancels_work_and_late_native_completion_does_not_cache()
    {
        var cache = new IconLoadCache(1024, 1);
        var native = Pending();
        CancellationToken shared = default;
        using var cancellation = new CancellationTokenSource();
        var request = cache.GetAsync("old", token => { shared = token; return native.Task; }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(shared.IsCancellationRequested);
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = cache.GetAsync("next", _ => { nextStarted.SetResult(); return Task.FromResult<IconBitmap?>(null); }, default);
        Assert.False(nextStarted.Task.IsCompleted); // A native call still owns the only slot.
        native.SetResult(Bitmap());
        await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(cache.TryGetCached("old"));
    }

    [Fact]
    public async Task Last_waiter_leaving_then_same_key_reloading_keeps_the_new_bitmap()
    {
        var cache = new IconLoadCache(1024, 2);
        var oldNative = Pending();
        using var cancellation = new CancellationTokenSource();
        var old = cache.GetAsync("same", _ => oldNative.Task, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);

        var replacement = Bitmap();
        Assert.Same(replacement, await cache.GetAsync("same", _ => Task.FromResult<IconBitmap?>(replacement), default));
        // Occupy the second slot so the observer runs only once the old native
        // call exits its slot and has attempted cache publication.
        var heldNative = Pending();
        var held = cache.GetAsync("held", _ => heldNative.Task, default);
        var observer = cache.GetAsync("observer", _ => Task.FromResult<IconBitmap?>(null), default);
        Assert.False(observer.IsCompleted);
        oldNative.SetResult(Bitmap());
        await observer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(replacement, cache.TryGetCached("same"));
        heldNative.SetResult(null);
        await held;
    }

    [Fact]
    public async Task Synchronous_completion_is_cached_before_the_last_waiter_leaves()
    {
        var cache = new IconLoadCache(1024, 1);
        var bitmap = Bitmap();
        Assert.Same(bitmap, await cache.GetAsync("same", _ => Task.FromResult<IconBitmap?>(bitmap), default));
        Assert.Same(bitmap, cache.TryGetCached("same"));
        Assert.Same(bitmap, await cache.GetAsync("same", _ => throw new InvalidOperationException("Must be cached."), default));
    }

    [Fact]
    public async Task Many_visible_rows_share_one_decode()
    {
        var cache = new IconLoadCache(1024, 4);
        var native = Pending();
        var calls = 0;
        var rows = Enumerable.Range(0, 1000)
            .Select(_ => cache.GetAsync("extension", _ => { calls++; return native.Task; }, default)).ToArray();
        Assert.Equal(1, calls);
        var bitmap = Bitmap();
        native.SetResult(bitmap);
        Assert.All(await Task.WhenAll(rows), actual => Assert.Same(bitmap, actual));
    }

    [Fact]
    public async Task Canceled_native_work_remains_bounded_while_new_rows_are_opened()
    {
        var cache = new IconLoadCache(1024, 4);
        var releases = Enumerable.Range(0, 4).Select(_ => Pending()).ToArray();
        using var cancellation = new CancellationTokenSource();
        var started = 0;
        var old = Enumerable.Range(0, 40).Select(i => cache.GetAsync($"old-{i}", _ =>
        {
            var index = Interlocked.Increment(ref started) - 1;
            return releases[index].Task;
        }, cancellation.Token)).ToArray();
        Assert.Equal(4, started);
        cancellation.Cancel();
        foreach (var task in old) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        var freshStarted = 0;
        var fresh = Enumerable.Range(0, 40).Select(i => cache.GetAsync($"new-{i}", _ =>
        {
            Interlocked.Increment(ref freshStarted);
            return Task.FromResult<IconBitmap?>(Bitmap());
        }, default)).ToArray();
        Assert.Equal(0, freshStarted);
        foreach (var release in releases) release.SetResult(Bitmap());
        await Task.WhenAll(fresh).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, started); // The other 36 canceled requests never extracted pixels.
        Assert.Equal(40, freshStarted);
    }

    [Fact]
    public async Task Pixel_budget_and_explicit_reclaim_bound_retained_memory()
    {
        var cache = new IconLoadCache(8 * 1024 * 1024, 2);
        for (var i = 0; i < 64; i++)
        {
            await cache.GetAsync($"photo-{i}", _ => Task.FromResult<IconBitmap?>(
                new IconBitmap(512, 512, new byte[512 * 512 * 4])), default);
            Assert.InRange(cache.CacheBytes, 0, 8 * 1024 * 1024);
        }
        Assert.Equal(8 * 1024 * 1024, cache.CacheBytes);
        cache.Clear();
        Assert.Equal(0, cache.CacheBytes);
    }

    [Fact]
    public async Task Canceled_caller_does_not_receive_cached_pixels()
    {
        var cache = new IconLoadCache(1024, 1);
        await cache.GetAsync("same", _ => Task.FromResult<IconBitmap?>(Bitmap()), default);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync("same", _ => throw new InvalidOperationException(), canceled.Token));
    }

    [Fact]
    public async Task Failed_shared_decode_releases_slot_and_does_not_poison_retry()
    {
        var cache = new IconLoadCache(1024, 1);
        var native = Pending();
        var first = cache.GetAsync("same", _ => native.Task, default);
        var second = cache.GetAsync("same", _ => throw new InvalidOperationException("Must share the first load."), default);
        native.SetException(new IOException("Decoder failed."));
        await Assert.ThrowsAsync<IOException>(() => first);
        await Assert.ThrowsAsync<IOException>(() => second);
        Assert.Null(cache.TryGetCached("same"));
        Assert.NotNull(await cache.GetAsync("same", _ => Task.FromResult<IconBitmap?>(Bitmap()), default));
    }

    [Fact]
    public async Task System_icon_cache_can_be_reclaimed_and_loaded_again()
    {
        if (!OperatingSystem.IsWindows()) return;
        var service = new WindowsSystemIconService();
        var key = new IconKey("e:.txt", 32);
        var bitmap = await service.GetAsync(key, "example.txt", FileAttributes.Normal, false, default);
        Assert.NotNull(bitmap);
        Assert.Same(bitmap, service.TryGetCached(key));
        service.ClearCache();
        Assert.Null(service.TryGetCached(key));
        Assert.NotNull(await service.GetAsync(key, "example.txt", FileAttributes.Normal, false, default));
    }
}
