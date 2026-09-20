using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class FileOperationLifetimeTests
{
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
