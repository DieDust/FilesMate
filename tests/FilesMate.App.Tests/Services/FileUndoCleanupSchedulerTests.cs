using FilesMate.App.Services;
using FilesMate.Core.Operations;

namespace FilesMate.App.Tests.Services;

public sealed class FileUndoCleanupSchedulerTests
{
    [Fact]
    public async Task Cleanup_is_serial_keeps_shutdown_busy_and_recovers_after_an_error()
    {
        var scheduler = new FileUndoCleanupScheduler();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sequence = new List<int>();
        scheduler.Schedule(() => { started.SetResult(); release.Wait(); sequence.Add(1); });
        try
        {
            Assert.True(FileOperationLifetime.IsBusy);
            var idle = FileOperationLifetime.WhenIdleAsync();
            scheduler.Schedule(() => { sequence.Add(2); throw new IOException("controlled cleanup failure"); });
            scheduler.Schedule(() => sequence.Add(3));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(sequence); Assert.False(idle.IsCompleted);
            release.Set();
            await Task.Run(scheduler.Drain).WaitAsync(TimeSpan.FromSeconds(10));
            await idle.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(new[] { 1, 2, 3 }, sequence);
        }
        finally { release.Set(); scheduler.Drain(); }
    }

    [Fact]
    public async Task Stack_eviction_returns_before_slow_cleanup_and_clear_drains_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate.CleanupQueue", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scheduler = new FileUndoCleanupScheduler(); var stack = new FileUndoStack(scheduler);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = 0;
        Task? clear = null;
        try
        {
            for (var i = 0; i <= FileUndoStack.Limit; i++)
            {
                var directory = Directory.CreateDirectory(Path.Combine(root, $"backup{i}")).FullName;
                var target = Path.Combine(root, $"target{i}"); var backup = Path.Combine(directory, "old");
                File.WriteAllText(target, "incoming"); File.WriteAllText(backup, "original");
                var first = i == 0;
                var lease = new Lease(() =>
                {
                    if (first) { started.SetResult(); release.Wait(); }
                    Interlocked.Increment(ref released);
                });
                stack.Push(FileUndoRecord.Copied([], []) with { Replacements = [new(target + ".source", target, backup, false, lease)] });
            }
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, Volatile.Read(ref released)); Assert.True(FileOperationLifetime.IsBusy);
            clear = Task.Run(stack.Clear);
            await Task.Delay(50);
            Assert.False(clear.IsCompleted);
            release.Set(); await clear.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(FileUndoStack.Limit + 1, released);
            Assert.False(stack.CanUndo); Assert.False(stack.CanRedo);
            Assert.Empty(Directory.EnumerateDirectories(root));
        }
        finally
        {
            release.Set();
            if (clear is not null) await clear;
            stack.Clear();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.CleanupQueue")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }

    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}
