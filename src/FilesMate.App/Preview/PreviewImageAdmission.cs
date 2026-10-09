namespace FilesMate.App.Preview;

/// <summary>One direct image pipeline per view, with a shared bounded, fair admission queue.</summary>
internal sealed class PreviewImageAdmission(PreviewImageAdmissionPool? pool = null)
{
    private static readonly PreviewImageAdmissionPool Shared = new(concurrency: 2);
    private readonly PreviewImageAdmissionPool _pool = pool ?? Shared;

    internal async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        // Preserve the caller's context: the callback creates UI-affine BitmapImage
        // objects. Only lease cleanup may run away from that context.
        var lease = await _pool.AcquireAsync(this, cancellationToken);
        Task<T>? work = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            work = operation();
            var result = await work.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            if (work is null || work.IsCompleted)
            {
                _ = work?.Exception; // A native failure may race logical cancellation.
                lease.Dispose();
            }
            else _ = CompleteAndReleaseAsync(work, lease);
        }
    }

    private static async Task CompleteAndReleaseAsync(Task work, IDisposable lease)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception) { /* Observe failures after the requesting view has left. */ }
        finally { lease.Dispose(); }
    }
}

/// <summary>FIFO between eligible owners; replacing an owner's queued request preserves its turn.</summary>
internal sealed class PreviewImageAdmissionPool
{
    private readonly object _sync = new();
    private readonly int _concurrency;
    private readonly HashSet<object> _active = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Waiter> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Waiter> _queue = new();

    internal PreviewImageAdmissionPool(int concurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        _concurrency = concurrency;
    }

    internal (int Active, int Queued) Statistics { get { lock (_sync) return (_active.Count, _queue.Count); } }

    internal async Task<IDisposable> AcquireAsync(object owner, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var waiter = new Waiter(owner, cancellationToken);
        using var registration = cancellationToken.UnsafeRegister(static state =>
        {
            var (pool, request) = ((PreviewImageAdmissionPool, Waiter))state!;
            pool.Cancel(request);
        }, (this, waiter));
        lock (_sync)
        {
            if (!waiter.Cancelled)
            {
                if (_pending.TryGetValue(owner, out var previous))
                {
                    // The queue contains at most one pending entry per owner,
                    // even if a caller forgot to cancel its superseded request.
                    waiter.Node = previous.Node;
                    waiter.Node!.Value = waiter;
                    previous.Node = null;
                    previous.Cancelled = true;
                    previous.Completion.TrySetCanceled(new CancellationToken(canceled: true));
                }
                else waiter.Node = _queue.AddLast(waiter);
                _pending[owner] = waiter;
                Drain();
            }
        }
        return await waiter.Completion.Task.ConfigureAwait(false);
    }

    private void Cancel(Waiter waiter)
    {
        lock (_sync)
        {
            // A granted lease belongs to the real operation, never to a cancel
            // callback. RunAsync releases an unused lease if cancellation wins.
            if (waiter.Granted || waiter.Cancelled) return;
            waiter.Cancelled = true;
            RemovePending(waiter);
            waiter.Completion.TrySetCanceled(waiter.Token);
            Drain();
        }
    }

    private void RemovePending(Waiter waiter)
    {
        if (waiter.Node is { } node)
        {
            _queue.Remove(node);
            waiter.Node = null;
        }
        if (_pending.TryGetValue(waiter.Owner, out var current) && ReferenceEquals(current, waiter))
            _pending.Remove(waiter.Owner);
    }

    // Called only under _sync. TCS continuations always run asynchronously.
    private void Drain()
    {
        var node = _queue.First;
        while (node is not null && _active.Count < _concurrency)
        {
            var next = node.Next;
            var waiter = node.Value;
            if (waiter.Token.IsCancellationRequested)
            {
                waiter.Cancelled = true;
                RemovePending(waiter);
                waiter.Completion.TrySetCanceled(waiter.Token);
            }
            else if (!_active.Contains(waiter.Owner))
            {
                RemovePending(waiter);
                waiter.Granted = true;
                _active.Add(waiter.Owner);
                waiter.Completion.TrySetResult(new Lease(this, waiter.Owner));
            }
            node = next;
        }
    }

    private void Release(object owner)
    {
        lock (_sync)
        {
            _active.Remove(owner);
            Drain();
        }
    }

    private sealed class Lease(PreviewImageAdmissionPool pool, object owner) : IDisposable
    {
        private PreviewImageAdmissionPool? _pool = pool;
        public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Release(owner);
    }

    private sealed class Waiter(object owner, CancellationToken token)
    {
        internal object Owner { get; } = owner;
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource<IDisposable> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<Waiter>? Node;
        internal bool Cancelled, Granted;
    }
}
