using FilesMate.Core.Icons;
using FilesMate.Platform.Windows.Icons;

namespace FilesMate.Platform.Windows.Tests.Icons;

public sealed class IconLoadGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_generation_never_joins_old_same_key_work(bool oldMetadataArrivesAfterClear)
    {
        var cache = new IconLoadCache(1024, 2);
        var oldGeneration = cache.Generation;
        var oldNative = new TaskCompletionSource<IconBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (oldMetadataArrivesAfterClear) cache.Clear();
        var old = cache.GetAsync("same", _ => oldNative.Task, default, oldGeneration);
        if (!oldMetadataArrivesAfterClear) cache.Clear();
        var freshBitmap = new IconBitmap(1, 1, [1, 2, 3, 4]);
        var freshCalls = 0;
        var fresh = cache.GetAsync("same", _ => { freshCalls++; return Task.FromResult<IconBitmap?>(freshBitmap); }, default);
        try
        {
            Assert.Equal(1, freshCalls);
            Assert.Same(freshBitmap, await fresh.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { oldNative.TrySetResult(new IconBitmap(1, 1, new byte[4])); await old; }
        Assert.Same(freshBitmap, cache.TryGetCached("same"));
    }
}
