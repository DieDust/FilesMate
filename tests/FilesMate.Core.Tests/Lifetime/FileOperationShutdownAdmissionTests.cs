using FilesMate.App.Services;

namespace FilesMate.Core.Tests.Lifetime;

[CollectionDefinition("Shutdown admission", DisableParallelization = true)]
public sealed class ShutdownAdmissionCollection { }

[Collection("Shutdown admission")]
public sealed class FileOperationShutdownAdmissionTests
{
    [Fact]
    public void Active_file_work_cannot_be_reserved_for_shutdown()
    {
        using var first = FileOperationLifetime.Begin();
        using var second = FileOperationLifetime.Begin();
        Assert.True(FileOperationLifetime.IsBusy);
        Assert.Null(FileOperationLifetime.TryReserveShutdown());
        Assert.False(FileOperationLifetime.WhenIdleAsync().IsCompleted);
    }

    [Fact]
    public void Committed_shutdown_rejects_new_work_without_cancelling_old_work()
    {
        Assert.False(FileOperationLifetime.IsBusy);
        using var shutdown = FileOperationLifetime.TryReserveShutdown();
        Assert.NotNull(shutdown);
        Assert.False(FileOperationLifetime.IsBusy);
        Assert.Throws<OperationCanceledException>(() => FileOperationLifetime.Begin());
        Assert.Null(FileOperationLifetime.TryBeginWhenIdle());
        Assert.Null(FileOperationLifetime.TryReserveShutdown());
    }

    [Fact]
    public void Cancelled_close_releases_admission_once_and_allows_resumed_commands()
    {
        var shutdown = FileOperationLifetime.TryReserveShutdown();
        Assert.NotNull(shutdown);
        shutdown.Dispose();
        shutdown.Dispose();
        using var resumed = FileOperationLifetime.Begin();
        Assert.True(FileOperationLifetime.IsBusy);
    }

    [Fact]
    public async Task File_work_and_shutdown_have_one_atomic_winner()
    {
        using var rendezvous = new Barrier(2);
        IDisposable? operation = null;
        IDisposable? shutdown = null;
        var rejected = false;
        var worker = Task.Run(() =>
        {
            rendezvous.SignalAndWait();
            try { operation = FileOperationLifetime.Begin(); }
            catch (OperationCanceledException) { rejected = true; }
        });
        rendezvous.SignalAndWait();
        shutdown = FileOperationLifetime.TryReserveShutdown();
        await worker;
        try
        {
            if (shutdown is not null) { Assert.True(rejected); Assert.Null(operation); }
            else { Assert.False(rejected); Assert.NotNull(operation); }
        }
        finally { operation?.Dispose(); shutdown?.Dispose(); }
    }
}
