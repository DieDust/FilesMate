namespace FilesMate.App.Services;

/// <summary>Keeps application shutdown from interrupting in-process file work.</summary>
public static class FileOperationLifetime
{
    private static readonly object Sync = new();
    private static int _active;
    private static bool _shutdownReserved;
    private static TaskCompletionSource? _idle;

    public static bool IsBusy { get { lock (Sync) return _active > 0; } }

    public static IDisposable Begin()
    {
        lock (Sync)
        {
            if (_shutdownReserved) throw new OperationCanceledException("Application shutdown owns file-work admission.");
            if (_active++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return new Lease();
    }

    public static IDisposable? TryBeginWhenIdle()
    {
        lock (Sync)
        {
            if (_active != 0 || _shutdownReserved) return null;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _active = 1;
            return new Lease();
        }
    }

    // The same lock owns both admission paths; existing leases are never cancelled.
    public static IDisposable? TryReserveShutdown()
    {
        lock (Sync)
        {
            if (_active != 0 || _shutdownReserved) return null;
            _shutdownReserved = true;
            return new ShutdownReservation();
        }
    }

    public static Task WhenIdleAsync()
    {
        lock (Sync) return _idle?.Task ?? Task.CompletedTask;
    }

    private sealed class ShutdownReservation : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (Sync) _shutdownReserved = false;
        }
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (Sync)
            {
                if (--_active != 0) return;
                _idle!.TrySetResult();
                _idle = null;
            }
        }
    }
}
