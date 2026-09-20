using FilesMate.Core.Scheduling;

namespace FilesMate.Core.Tests.Scheduling;

public sealed class BoundedWorkQueueTests
{
    [Fact]
    public async Task Full_queue_applies_backpressure_instead_of_growing()
    {
        var queue = new BoundedWorkQueue<int>(capacity: 4);
        var produced = 0;
        var producer = Task.Run(async () =>
        {
            for (var i = 0; i < 16; i++)
            {
                await queue.WriteAsync(i, CancellationToken.None);
                Interlocked.Increment(ref produced);
            }

            queue.Complete();
        });

        await Task.Delay(80);
        Assert.InRange(Volatile.Read(ref produced), 1, 5);

        var consumed = 0;
        await foreach (var _ in queue.ReadAllAsync(CancellationToken.None))
        {
            consumed++;
        }

        await producer;
        Assert.Equal(16, consumed);
        Assert.Equal(16, Volatile.Read(ref produced));
    }

    [Fact]
    public async Task Complete_makes_reader_finish()
    {
        var queue = new BoundedWorkQueue<int>(capacity: 2);
        await queue.WriteAsync(1, CancellationToken.None);
        queue.Complete();
        var items = new List<int>();
        await foreach (var item in queue.ReadAllAsync(CancellationToken.None))
        {
            items.Add(item);
        }

        Assert.Equal([1], items);
    }
}
