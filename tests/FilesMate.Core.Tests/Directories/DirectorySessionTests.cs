using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Core.Scheduling;

namespace FilesMate.Core.Tests.Directories;

public sealed class DirectorySessionTests
{
    [Fact]
    public void Only_the_newer_generation_is_accepted_as_current()
    {
        var pane = PaneId.New();
        var gate = new GenerationGate(pane);
        var first = gate.Begin();
        var second = gate.Begin();

        Assert.False(gate.Allows(pane, first));
        Assert.True(gate.Allows(pane, second));
        Assert.Equal(second, gate.CurrentGeneration);
    }

    [Fact]
    public async Task Cancellation_between_batches_does_not_mutate_the_store()
    {
        var pane = PaneId.New();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = new FakeDirectoryEnumerator
        {
            Batches =
            [
                Batch(pane, 1, @"D:\a", [Entry(1, "one.txt")], isFinal: false),
                Batch(pane, 1, @"D:\a", [Entry(2, "two.txt")], isFinal: true),
            ],
            DelayAfterBatch = _ => hold.Task,
        };

        await using var session = DirectorySession.Start(
            new DirectoryRequest(pane, 1, @"D:\a", DirectoryReadOptions.Default with { BatchSize = 1 }),
            enumerator);

        await session.WaitForEntryCountAsync(1);
        Assert.Equal(1, session.Store.Count);

        await session.DisposeAsync();
        hold.TrySetResult();
        await Task.Delay(50);

        Assert.Equal(1, session.Store.Count);
        Assert.Equal("one.txt", session.Store[0].Name);
    }

    [Fact]
    public async Task Session_drains_a_bounded_batch_queue_to_completion()
    {
        var pane = PaneId.New();
        var enumerator = new FakeDirectoryEnumerator
        {
            Batches = Enumerable.Range(0, 16)
                .Select(i => Batch(pane, 1, @"D:\a", [Entry(i + 1, $"f{i}.txt")], isFinal: i == 15))
                .ToList(),
        };

        await using var session = DirectorySession.Start(
            new DirectoryRequest(pane, 1, @"D:\a", DirectoryReadOptions.Default with { BatchSize = 1 }),
            enumerator,
            batchChannelCapacity: 4);

        await session.WhenCompleted;
        Assert.Equal(16, session.Store.Count);
        Assert.Equal(DirectorySessionState.Completed, session.State);
    }

    [Fact]
    public async Task Dispose_cancels_enumeration_and_is_idempotent()
    {
        var pane = PaneId.New();
        var enumerator = new FakeDirectoryEnumerator
        {
            Batches =
            [
                Batch(pane, 1, @"D:\a", [Entry(1, "a.txt")], isFinal: false),
                Batch(pane, 1, @"D:\a", [Entry(2, "b.txt")], isFinal: true),
            ],
            DelayAfterBatch = _ => Task.Delay(TimeSpan.FromSeconds(30)),
        };

        var session = DirectorySession.Start(
            new DirectoryRequest(pane, 1, @"D:\a", DirectoryReadOptions.Default),
            enumerator);

        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.True(enumerator.Canceled);
        Assert.True(session.WhenCompleted.IsCompleted);
    }

    [Fact]
    public async Task Partial_results_remain_readable_after_terminal_error()
    {
        var pane = PaneId.New();
        var enumerator = new FakeDirectoryEnumerator
        {
            Batches =
            [
                Batch(pane, 1, @"D:\a", [Entry(1, "kept.txt")], isFinal: false),
                DirectoryBatch.Create(
                    pane,
                    1,
                    @"D:\a",
                    [],
                    isFinal: true,
                    new DirectoryReadError(DirectoryReadErrorKind.AccessDenied, 5, "denied", isTerminal: true)),
            ],
        };

        await using var session = DirectorySession.Start(
            new DirectoryRequest(pane, 1, @"D:\a", DirectoryReadOptions.Default),
            enumerator);

        await session.WhenCompleted;
        Assert.Equal(DirectorySessionState.Failed, session.State);
        Assert.Equal(1, session.Store.Count);
        Assert.Equal("kept.txt", session.Store[0].Name);
        Assert.NotNull(session.Error);
    }

    [Fact]
    public void Reopening_the_same_path_uses_a_new_generation()
    {
        var pane = PaneId.New();
        var gate = new GenerationGate(pane);
        var first = gate.Begin();
        var second = gate.Begin();
        Assert.NotEqual(first, second);
        Assert.False(gate.Allows(pane, first));
    }

    private static DirectoryBatch Batch(PaneId pane, long generation, string path, FileEntryCore[] entries, bool isFinal) =>
        DirectoryBatch.Create(pane, generation, path, entries, isFinal, error: null);

    private static FileEntryCore Entry(int id, string name) => new(
        id,
        name,
        Size: 1,
        ModifiedUtcTicks: 0,
        CreatedUtcTicks: 0,
        Attributes: System.IO.FileAttributes.Normal,
        Kind: EntryKind.File);
}

internal sealed class FakeDirectoryEnumerator : IDirectoryEnumerator
{
    public List<DirectoryBatch> Batches { get; init; } = [];

    public Func<DirectoryBatch, Task>? DelayAfterBatch { get; init; }

    public Action<DirectoryBatch>? OnBatchEmitted { get; init; }

    public bool Canceled { get; private set; }

    public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
        DirectoryRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var batch in Batches)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Canceled = true;
                yield break;
            }

            OnBatchEmitted?.Invoke(batch);
            yield return batch;
            if (DelayAfterBatch is not null)
            {
                try
                {
                    await DelayAfterBatch(batch).WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Canceled = true;
                    yield break;
                }
            }
        }
    }
}
