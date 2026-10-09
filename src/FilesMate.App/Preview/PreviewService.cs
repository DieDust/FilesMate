namespace FilesMate.App.Preview;

public sealed class PreviewService : IAsyncDisposable
{
    private readonly IReadOnlyList<IPreviewProvider> _providers;
    private readonly object _gate = new();
    private readonly Dictionary<object, CancellationTokenSource> _active = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public PreviewService(IEnumerable<IPreviewProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        if (_providers.Count == 0)
        {
            throw new ArgumentException("At least one preview provider is required.", nameof(providers));
        }
    }

    public async Task<PreviewResult> LoadAsync(
        PreviewRequest request,
        CancellationToken cancellationToken = default,
        object? owner = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        request = request.Normalize();
        owner ??= this;
        CancellationTokenSource current;
        CancellationTokenSource? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active.TryGetValue(owner, out previous);
            current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _active[owner] = current;
        }

        CancelRequest(previous);

        Task<PreviewResult>? pending = null;
        try
        {
            current.Token.ThrowIfCancellationRequested();
            var provider = _providers.FirstOrDefault(item => item.CanHandle(request.Path));
            if (provider is null)
            {
                return new PreviewResult.Unsupported(request.Path, "No preview provider is registered for this file type.");
            }

            pending = provider.CreateAsync(request, current.Token);
            var result = await pending.WaitAsync(current.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed || current.IsCancellationRequested ||
                    !_active.TryGetValue(owner, out var active) || !ReferenceEquals(active, current))
                {
                    throw new OperationCanceledException(current.Token);
                }
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                if (_active.TryGetValue(owner, out var active) && ReferenceEquals(active, current))
                {
                    _active.Remove(owner);
                }
            }

            if (pending is null || pending.IsCompleted)
            {
                _ = pending?.Exception; // Observe a fault racing cancellation.
                current.Dispose();
            }
            else
            {
                // Let the view leave promptly even when a provider cannot stop
                // native work. That work, not the departed view, owns CTS disposal.
                _ = pending.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    current.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    internal int ActiveOwnerCount { get { lock (_gate) return _active.Count; } }

    /// <summary>Release one view's admission immediately; its async frame owns CTS disposal.</summary>
    public void Cancel(object? owner = null)
    {
        CancellationTokenSource? active;
        lock (_gate) _active.Remove(owner ?? this, out active);
        CancelRequest(active);
    }

    public ValueTask DisposeAsync()
    {
        CancellationTokenSource[] active;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            active = _active.Values.ToArray();
            _active.Clear();
        }

        foreach (var request in active)
        {
            CancelRequest(request);
        }

        return ValueTask.CompletedTask;
    }

    private static void CancelRequest(CancellationTokenSource? request)
    {
        try { request?.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException error)
        {
            // A faulty provider callback must not prevent other owners from
            // closing or leave a newly admitted replacement outside its finally.
            System.Diagnostics.Trace.TraceError("Preview provider cancellation callback failed: {0}", error);
        }
    }
}
