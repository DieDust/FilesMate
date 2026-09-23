using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

[Collection("File operation lifetime")]
public sealed class FileOperationLifetimeTests
{
    [Fact]
    public async Task Exclusive_idle_reservation_blocks_reentry_and_releases_after_failure()
    {
        using (var transfer = FileOperationLifetime.Begin())
            Assert.Null(FileOperationLifetime.TryBeginWhenIdle());
        Task idle;
        try
        {
            using var removal = FileOperationLifetime.TryBeginWhenIdle();
            Assert.NotNull(removal);
            idle = FileOperationLifetime.WhenIdleAsync();
            Assert.False(idle.IsCompleted);
            Assert.Null(FileOperationLifetime.TryBeginWhenIdle());
            throw new IOException("Simulated device removal failure");
        }
        catch (IOException) { }
        Assert.False(FileOperationLifetime.IsBusy);
        await FileOperationLifetime.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var retry = FileOperationLifetime.TryBeginWhenIdle();
        Assert.NotNull(retry);
    }

    [Fact]
    public async Task Shutdown_waits_for_all_active_operations_and_duplicate_dispose_is_safe()
    {
        var first = FileOperationLifetime.Begin();
        var second = FileOperationLifetime.Begin();
        try
        {
            var idle = FileOperationLifetime.WhenIdleAsync();
            Assert.False(idle.IsCompleted);
            first.Dispose();
            first.Dispose();
            Assert.False(idle.IsCompleted);
            second.Dispose();
            await idle.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { first.Dispose(); second.Dispose(); }
    }
}
