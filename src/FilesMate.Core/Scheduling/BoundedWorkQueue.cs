using System.Threading.Channels;

namespace FilesMate.Core.Scheduling;

/// <summary>
/// Bounded producer/consumer queue. When full, writers wait instead of expanding memory.
/// Thread-safety: single-writer/single-reader is the intended pattern; Channel supplies the rest.
/// Ownership: Complete/Dispose ends the reader. Cancellation: WriteAsync/ReadAllAsync honor the token.
/// </summary>
public sealed class BoundedWorkQueue<T>
{
    private readonly Channel<T> _channel;

    public BoundedWorkQueue(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public ValueTask WriteAsync(T item, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Exception? error = null)
    {
        if (error is null)
        {
            _channel.Writer.TryComplete();
        }
        else
        {
            _channel.Writer.TryComplete(error);
        }
    }
}
