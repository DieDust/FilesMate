using System.Threading.Channels;

using FilesMate.Core.Navigation;
using FilesMate.Core.Scheduling;

namespace FilesMate.Core.Directories;

/// <summary>
/// Owns one pane generation: cancellation, entry store, and the enumeration task.
/// Thread-safety: public state is updated on the worker and observed with volatiles/TCS.
/// Ownership: dispose cancels enumeration, completes queues, and is idempotent.
/// Cancellation: required; later batches must not append after dispose.
/// Errors: converted to <see cref="DirectorySessionState.Failed"/>; background exceptions are observed on
/// <see cref="WhenCompleted"/>. Partial entries remain readable.
/// Staleness: published entries may already be gone from the filesystem.
/// </summary>
public sealed class DirectorySession : IAsyncDisposable
{
    private readonly DirectoryRequest _request;
    private readonly IDirectoryEnumerator _enumerator;
    private readonly BoundedWorkQueue<DirectoryBatch> _batches;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<DirectorySessionChange> _changes = Channel.CreateBounded<DirectorySessionChange>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });

    private Task? _run;
    private int _disposed;

    private DirectorySession(DirectoryRequest request, IDirectoryEnumerator enumerator, int batchChannelCapacity)
    {
        _request = request;
        _enumerator = enumerator;
        _batches = new BoundedWorkQueue<DirectoryBatch>(batchChannelCapacity);
        Store = new EntryStore();
        State = DirectorySessionState.Loading;
        WhenCompleted = _completed.Task;
    }

    public PaneId PaneId => _request.PaneId;

    public long Generation => _request.Generation;

    public string Path => _request.Path;

    public DirectoryReadOptions Options => _request.Options;

    public EntryStore Store { get; }

    public DirectorySessionState State { get; private set; }

    public DirectoryReadError? Error { get; private set; }

    public Task WhenCompleted { get; }

    /// <summary>
    /// Coalesced session notifications. One consumer per session; tests that call
    /// <see cref="WaitForEntryCountAsync"/> must not also consume this sequence.
    /// </summary>
    public IAsyncEnumerable<DirectorySessionChange> ReadChangesAsync(CancellationToken cancellationToken = default) =>
        _changes.Reader.ReadAllAsync(cancellationToken);

    public static DirectorySession Start(
        DirectoryRequest request,
        IDirectoryEnumerator enumerator,
        int batchChannelCapacity = 4)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        if (batchChannelCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchChannelCapacity));
        }

        var session = new DirectorySession(request, enumerator, batchChannelCapacity);
        session._run = session.RunAsync();
        return session;
    }

    public async Task WaitForEntryCountAsync(int count, CancellationToken cancellationToken = default)
    {
        if (Store.Count >= count)
        {
            return;
        }

        await foreach (var _ in _changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Store.Count >= count)
            {
                return;
            }

            if (WhenCompleted.IsCompleted && Store.Count < count)
            {
                throw new InvalidOperationException($"Session ended with {Store.Count} entries; {count} were required.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        _batches.Complete();
        _changes.Writer.TryComplete();

        if (_run is not null)
        {
            try
            {
                await _run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _completed.TrySetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            var produce = ProduceAsync();
            var consume = ConsumeAsync();
            await Task.WhenAll(produce, consume).ConfigureAwait(false);
            if (State is not DirectorySessionState.Failed and not DirectorySessionState.Cancelled)
            {
                State = DirectorySessionState.Completed;
                PublishChange();
            }
        }
        catch (OperationCanceledException)
        {
            State = DirectorySessionState.Cancelled;
        }
        catch (Exception ex)
        {
            State = DirectorySessionState.Failed;
            Error ??= new DirectoryReadError(DirectoryReadErrorKind.Unknown, 0, ex.Message, isTerminal: true);
            _completed.TrySetException(ex);
            return;
        }
        finally
        {
            _batches.Complete();
            _changes.Writer.TryComplete();
            _completed.TrySetResult();
        }
    }

    private async Task ProduceAsync()
    {
        try
        {
            await foreach (var batch in _enumerator.EnumerateAsync(_request, _cts.Token).ConfigureAwait(false))
            {
                _cts.Token.ThrowIfCancellationRequested();
                await _batches.WriteAsync(batch, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            State = DirectorySessionState.Cancelled;
        }
        finally
        {
            _batches.Complete();
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var batch in _batches.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                if (batch.PaneId != PaneId || batch.Generation != Generation)
                {
                    continue;
                }

                Store.Append(batch.Entries);
                if (batch.Error is not null)
                {
                    Error = batch.Error;
                    if (batch.Error.IsTerminal)
                    {
                        State = DirectorySessionState.Failed;
                    }
                }
                else if (State == DirectorySessionState.Loading)
                {
                    State = DirectorySessionState.Partial;
                }

                PublishChange();
            }
        }
        catch (OperationCanceledException)
        {
            State = DirectorySessionState.Cancelled;
        }
    }

    private void PublishChange() =>
        _changes.Writer.TryWrite(new DirectorySessionChange(Store.Count, State, Error));
}
