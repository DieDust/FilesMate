namespace FilesMate.App.Services;

/// <summary>Keeps application shutdown from interrupting in-process file work.</summary>
public static class FileOperationLifetime
{
    private static readonly object Sync = new();
    private static int _active;
    private static TaskCompletionSource? _idle;

    public static bool IsBusy { get { lock (Sync) return _active > 0; } }

    public static IDisposable Begin()
    {
        lock (Sync)
        {
            if (_active++ == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return new Lease();
    }

    // Checking IsBusy and acquiring a lease separately leaves a race with
    // background work. Safe removal must reserve the idle state atomically.
    public static IDisposable? TryBeginWhenIdle()
    {
        lock (Sync)
        {
            if (_active != 0) return null;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _active = 1;
            return new Lease();
        }
    }

    public static Task WhenIdleAsync()
    {
        lock (Sync) return _idle?.Task ?? Task.CompletedTask;
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
