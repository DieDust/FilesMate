using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class PreviewCancellationTests
{
    [Fact]
    public async Task Provider_finishing_after_caller_cancellation_cannot_return_stale_result()
    {
        var provider = new DelayedProvider();
        await using var service = new PreviewService([provider]);
        using var cancellation = new CancellationTokenSource();
        var request = service.LoadAsync(new PreviewRequest("old.txt", 1), cancellation.Token);
        cancellation.Cancel();
        provider.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task Provider_finishing_after_preview_closed_cannot_return_result()
    {
        var provider = new DelayedProvider();
        var service = new PreviewService([provider]);
        var request = service.LoadAsync(new PreviewRequest("old.txt", 1));
        await service.DisposeAsync();
        provider.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task Reused_generation_does_not_allow_previous_file_result()
    {
        var provider = new DelayedProvider();
        await using var service = new PreviewService([provider]);
        var previous = service.LoadAsync(new PreviewRequest("old.txt", 1));
        var current = service.LoadAsync(new PreviewRequest("new.txt", 1));
        provider.Complete();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => previous);
        Assert.Equal("new.txt", (await current).Path);
    }

    private sealed class DelayedProvider : IPreviewProvider
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanHandle(string path) => true;
        public void Complete() => _release.TrySetResult();
        public async Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken)
        {
            // Some decoders/COM providers cannot interrupt an operation already in progress.
            await _release.Task;
            return new PreviewResult.Text(request.Path, "decoded", false);
        }
    }
}
