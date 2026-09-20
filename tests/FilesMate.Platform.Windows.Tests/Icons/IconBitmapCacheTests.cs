using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;

namespace FilesMate.Platform.Windows.Tests.Icons;

public sealed class IconBitmapCacheTests
{
    private static IconBitmap Pixels(int width = 1) => new(width, 1, new byte[width * 4]);

    [Fact]
    public void Byte_budget_evicts_least_recently_used_and_keeps_recent_reads()
    {
        var cache = new IconBitmapCache(maxBytes: 8);
        cache.Set("a", Pixels());
        cache.Set("b", Pixels());
        Assert.True(cache.TryGetValue("a", out _));
        cache.Set("c", Pixels());
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("a", out _));
        Assert.True(cache.TryGetValue("c", out _));
        Assert.Equal(8, cache.CurrentBytes);
    }

    [Fact]
    public void Replacement_and_oversize_values_do_not_inflate_budget()
    {
        var cache = new IconBitmapCache(maxBytes: 8);
        cache.Set("a", Pixels(2));
        cache.Set("a", Pixels());
        Assert.Equal(4, cache.CurrentBytes);
        cache.Set("large", Pixels(3));
        Assert.False(cache.TryGetValue("large", out _));
        Assert.Equal(4, cache.CurrentBytes);
    }

    [Fact]
    public void Entry_limit_also_bounds_small_images_and_path_keys()
    {
        var cache = new IconBitmapCache(maxBytes: 1024, maxEntries: 2);
        for (var i = 0; i < 100; i++) cache.Set(i.ToString(), Pixels());
        Assert.Equal(2, cache.Count);
        Assert.Equal(8, cache.CurrentBytes);
        Assert.True(cache.TryGetValue("99", out _));
        Assert.False(cache.TryGetValue("0", out _));
    }

    [Fact]
    public void Concurrent_reads_and_replacements_stay_within_both_limits()
    {
        var cache = new IconBitmapCache(maxBytes: 128, maxEntries: 16);
        Parallel.For(0, 10000, i =>
        {
            var key = (i % 100).ToString();
            cache.Set(key, Pixels(i % 4 + 1));
            cache.TryGetValue(key, out _);
        });
        Assert.InRange(cache.CurrentBytes, 0, 128);
        Assert.InRange(cache.Count, 0, 16);
    }

    [Fact]
    public void Concurrent_clearing_and_loading_preserve_accounting_and_future_eviction()
    {
        var cache = new IconBitmapCache(maxBytes: 128, maxEntries: 16);
        Parallel.For(0, 10000, i =>
        {
            if (i % 7 == 0) cache.Clear();
            else cache.Set((i % 100).ToString(), Pixels(i % 4 + 1));
        });
        long retainedBytes = 0;
        for (var i = 0; i < 100; i++)
            if (cache.TryGetValue(i.ToString(), out var bitmap)) retainedBytes += bitmap!.Bgra.LongLength;
        Assert.Equal(retainedBytes, cache.CurrentBytes);
        Assert.InRange(retainedBytes, 0, 128);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentBytes);
        for (var i = 0; i < 100; i++) cache.Set(i.ToString(), Pixels(8));
        Assert.Equal(128, cache.CurrentBytes);
        Assert.Equal(4, cache.Count);
    }
}
