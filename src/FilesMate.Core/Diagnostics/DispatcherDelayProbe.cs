using System.Diagnostics;

namespace FilesMate.Core.Diagnostics;

/// <summary>Queue delay only, not task/frame duration. Waits for each post before posting another.</summary>
public sealed class DispatcherDelayProbe : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    public Task WhenStopped { get; }

    public DispatcherDelayProbe(Func<Action, bool> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        WhenStopped = Task.Run(() => RunAsync(post));
    }

    private async Task RunAsync(Func<Action, bool> post)
    {
        var token = _stop.Token;
        try
        {
            while (true)
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var submitted = Stopwatch.GetTimestamp();
                if (!post(() =>
                {
                    if (!token.IsCancellationRequested) PerformanceTrace.Record(PerformanceStage.DispatcherDelay,
                        durationTicks: Stopwatch.GetTimestamp() - submitted);
                    done.TrySetResult();
                })) break;
                await done.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally { _stop.Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
    }
}
