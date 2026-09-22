using FilesMate.Core.Operations;

namespace FilesMate.App.Services;

/// <summary>Serializes expired backup cleanup without putting disk waits on the UI thread.</summary>
public sealed class FileUndoCleanupScheduler : IFileUndoCleanupScheduler
{
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;

    public void Schedule(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        // Acquire synchronously, before queuing: close, restart and update checks must
        // already see the pending cleanup even when its worker has not started yet.
        var lifetime = FileOperationLifetime.Begin();
        try
        {
            lock (_sync)
                _tail = _tail.ContinueWith(_ =>
                {
                    try { cleanup(); }
                    catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Undo backup cleanup failed: {0}", error); }
                    finally { lifetime.Dispose(); }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch { lifetime.Dispose(); throw; }
    }

    public void Drain()
    {
        Task pending;
        lock (_sync) pending = _tail;
        pending.GetAwaiter().GetResult();
    }
}
