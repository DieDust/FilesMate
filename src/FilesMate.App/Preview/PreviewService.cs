namespace FilesMate.App.Preview;

public sealed class PreviewService : IAsyncDisposable
{
    private readonly IReadOnlyList<IPreviewProvider> _providers;
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private long _generation;
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
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        request = request.Normalize();
        CancellationTokenSource current;
        CancellationTokenSource? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _active;
            current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _active = current;
            _generation = request.Generation;
        }

        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            current.Token.ThrowIfCancellationRequested();
            var provider = _providers.FirstOrDefault(item => item.CanHandle(request.Path));
            if (provider is null)
            {
                return new PreviewResult.Unsupported(request.Path, "No preview provider is registered for this file type.");
            }

            var result = await provider.CreateAsync(request, current.Token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_generation != request.Generation)
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
                if (ReferenceEquals(_active, current))
                {
                    _active = null;
                }
            }

            current.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        CancellationTokenSource? active;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            active = _active;
            _active = null;
        }

        try
        {
            active?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
