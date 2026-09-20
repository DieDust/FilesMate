using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class ByteBudgetCacheTests
{
    [Fact]
    public void Read_keeps_hot_entry_when_byte_budget_evicts_old_pixels()
    {
        var cache = new ByteBudgetCache<object>(10, 10);
        var hot = new object();
        cache.Set("hot", hot, 4);
        cache.Set("cold", new(), 4);
        Assert.True(cache.TryGetValue("hot", out _));
        cache.Set("new", new(), 4);
        Assert.True(cache.TryGetValue("hot", out var found));
        Assert.Same(hot, found);
        Assert.False(cache.TryGetValue("cold", out _));
        Assert.Equal(8, cache.Statistics.Bytes);
    }

    [Fact]
    public void Replacing_and_oversized_entries_do_not_leave_incorrect_accounting()
    {
        var cache = new ByteBudgetCache<object>(10, 2);
        cache.Set("a", new(), 3);
        cache.Set("b", new(), 3);
        cache.Set("a", new(), 6);
        Assert.Equal(9, cache.Statistics.Bytes);
        cache.Set("a", new(), 11);
        Assert.False(cache.TryGetValue("a", out _));
        Assert.Equal(3, cache.Statistics.Bytes);
        cache.Set("c", new(), 1);
        cache.Set("d", new(), 1);
        Assert.False(cache.TryGetValue("b", out _));
        Assert.Equal(2, cache.Statistics.Count);
        cache.Clear();
        Assert.Equal(0, cache.Statistics.Bytes);
        Assert.Equal(0, cache.Statistics.Count);
    }
}
