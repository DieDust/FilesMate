using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace FilesMate.Core.Diagnostics;

public enum PerformanceStage
{
    ManagedEntry, DatabaseRuntimeReady, AppConstructor, WindowConstructed, WindowActivated, LoadedProxy,
    NavigationReceived, EnumerationStart, EnumerationBatch, EnumerationDropped, EnumerationEnd,
    BuildRequested, BuildStart, BuildEnd, BuildCancelled, BuildAborted, PublishQueued, PublishDropped, ContentApplied,
    FinalApplied, IndexLockTiming, InputHandled, SearchUiReceived, SearchReceived, SearchIndexed, SearchLive,
    SearchReturned, SearchEnd, SearchUiApplied, SearchUiCancelled, DispatcherDelay,
    FilePointerPressed, FilePointerReleased, FileOpenRecognized, NavigationQueued, NavigationExecuting,
    SkinApplyStarted, SkinApplyCompleted, SkinPreviewCancelled, AppearanceCommitStarted, AppearanceCommitCompleted,
    FilePressPrepared, FileClickEvaluated, FileClickTrackerDecision, FileClickCancelled, FilePointerPosition, FileCaptureLost,
    RecursiveExpansionInput, RecursiveDesiredState, RecursiveProjectionQueued, RecursiveProjectionStarted,
    RecursiveProjectionCompleted, RecursiveDispatcherQueued, RecursiveDispatcherEntered,
    RecursiveUiApplyStarted, RecursiveUiApplyCompleted, RecursiveLayoutObserved, RecursiveRenderCallbackProxy,
    RecursiveCollectionReset, RecursiveThumbnailInvalidated, RecursiveRowPrepared, RecursiveRowCleared,
    RecursiveMenuRequested, RecursiveMenuOpened, RecursiveMenuBounds,
    ScrollbarDragStarted, ScrollbarDragUpdated, ScrollbarDragCompleted, ScrollViewportApplied,
    ScrollRowPrepared, ScrollFrameCallbackProxy, ScrollDemandShapeSkipped, ScrollDemandShapeApplied,
    ScrollHeaderSynchronized,
    ColumnResizeStarted, ColumnResizeUpdated, ColumnResizeCompleted, ColumnResizeCaptureLost,
    OrdinaryUiApplyStarted, OrdinaryUiApplyCompleted, OrdinaryFirstRowPrepared,
    OrdinaryFirstLayoutObserved, OrdinaryFirstContentRenderProxy, OrdinaryViewportFilledProxy,
    OrdinaryFrameObservationExpired, RecursiveExpansionNodeState, RecursiveExpansionRejected,
    PreviewKeyReceived, PreviewCommand, PreviewRequest, PreviewWindowShown, PreviewContentLoaded, PreviewBlocked, PreviewFailed,
    ScrollThumbnailRequests, ScrollImageCacheBytes, ScrollManagedBytes, ScrollGcCounts, ScrollFinalViewportReady
}

public enum PerformanceOutcome { None, Completed, Partial, Cancelled, Failed, Dropped }

/// <summary>Opt-in PR00 observations. IDs never control production cancellation or publishing.</summary>
public static class PerformanceTrace
{
    private static readonly PerformanceTraceWriter? Writer = CreateWriter();
    private static long _operation;
    public static bool Enabled => Writer is not null;

    public static long NextOperation() => Enabled ? Interlocked.Increment(ref _operation) : 0;

    public static void Record(PerformanceStage stage, Guid pane = default, long generation = 0,
        long operation = 0, long count = 0, PerformanceOutcome outcome = PerformanceOutcome.None,
        long durationTicks = 0, long auxiliary = 0)
    {
        // No formatting, disk access, or additional lock on the calling thread.
        Writer?.Record(stage, pane, generation, operation, count, outcome, durationTicks, auxiliary);
    }

    private static PerformanceTraceWriter? CreateWriter()
    {
        if (Environment.GetEnvironmentVariable("FILESMATE_PR00_TRACE") != "1") return null;
        var path = Environment.GetEnvironmentVariable("FILESMATE_PR00_TRACE_FILE");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || !Guid.TryParse(Environment.GetEnvironmentVariable("FILESMATE_PR00_RUN_ID"), out var run)) return null;
        // The terminal-job launcher, detached App, and Host inherit the same environment.
        // An opt-in PID token gives each writer its own CreateNew file without changing
        // the existing single-process path contract or allowing a file overwrite.
        path = path.Replace("{pid}", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var writer = new PerformanceTraceWriter(path, run);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => writer.Complete(TimeSpan.FromSeconds(1));
        return writer;
    }
}

/// <summary>
/// One bounded diagnostic file, created on a worker. Full buffers drop observations, never block UI.
/// Only enum stages/outcomes and numeric run-local correlations are accepted; no user strings.
/// </summary>
public sealed class PerformanceTraceWriter : IAsyncDisposable
{
    public const int DefaultCapacity = 8192;
    public const long DefaultMaxBytes = 32L * 1024 * 1024;
    private const int FooterReserve = 1024;
    private readonly Slot[] _slots;
    private long _enqueue, _dequeue;
    private int _activeWriters;
    private readonly Task _writeTask;
    private readonly Guid _run;
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _capacity;
    private long _sequence, _accepted, _dropped, _afterClose, _written;
    private int _closed, _truncated, _writerErrors;
    private readonly long _allocatedAtStart = GC.GetTotalAllocatedBytes(false);
    private readonly int _gen0AtStart = GC.CollectionCount(0), _gen1AtStart = GC.CollectionCount(1), _gen2AtStart = GC.CollectionCount(2);
    private readonly TimeSpan _pauseAtStart = GC.GetTotalPauseDuration();

    private readonly record struct Packet(long Sequence, long Timestamp, int Thread,
        PerformanceStage Stage, Guid Pane, long Generation, long Operation, long Count,
        PerformanceOutcome Outcome, long DurationTicks, long Auxiliary);

    private struct Slot { public long Ready; public Packet Value; }

    public PerformanceTraceWriter(string path, Guid runId, int capacity = DefaultCapacity,
        long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute diagnostic path.", nameof(path));
        if (capacity is < 1 or > DefaultCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxBytes is < 2048 or > DefaultMaxBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _path = path;
        _run = runId;
        _capacity = capacity;
        _maxBytes = maxBytes;
        _slots = new Slot[capacity];
        for (var i = 0; i < capacity; i++) _slots[i].Ready = i;
        _writeTask = Task.Run(WriteAsync);
    }

    public bool Record(PerformanceStage stage, Guid pane = default, long generation = 0,
        long operation = 0, long count = 0, PerformanceOutcome outcome = PerformanceOutcome.None,
        long durationTicks = 0, long auxiliary = 0)
    {
        Interlocked.Increment(ref _activeWriters);
        try
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                Interlocked.Increment(ref _afterClose);
                return false;
            }
            var packet = new Packet(Interlocked.Increment(ref _sequence), Stopwatch.GetTimestamp(),
                Environment.CurrentManagedThreadId, stage, pane, generation, operation, count,
                outcome, durationTicks, auxiliary);
            if (TryEnqueue(packet))
            {
                Interlocked.Increment(ref _accepted);
                return true;
            }
            Interlocked.Increment(ref _dropped);
            return false;
        }
        finally { Interlocked.Decrement(ref _activeWriters); }
    }

    // Fixed MPSC diagnostic ring: producers never acquire another lock inside existing UI gates.
    // Contention attempts are bounded; either a full buffer or contention is counted as a drop.
    private bool TryEnqueue(Packet packet)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var position = Volatile.Read(ref _enqueue);
            if (position - Volatile.Read(ref _dequeue) >= _capacity) return false;
            ref var slot = ref _slots[(int)(position % _capacity)];
            var difference = Volatile.Read(ref slot.Ready) - position;
            if (difference < 0) return false;
            if (difference == 0 && Interlocked.CompareExchange(ref _enqueue, position + 1, position) == position)
            {
                slot.Value = packet;
                Volatile.Write(ref slot.Ready, position + 1);
                return true;
            }
        }
        return false;
    }

    private bool TryDequeue(out Packet packet)
    {
        var position = _dequeue;
        ref var slot = ref _slots[(int)(position % _capacity)];
        if (Volatile.Read(ref slot.Ready) != position + 1) { packet = default; return false; }
        packet = slot.Value;
        Volatile.Write(ref slot.Ready, position + _capacity);
        Volatile.Write(ref _dequeue, position + 1);
        return true;
    }

    public bool Complete(TimeSpan timeout)
    {
        Close();
        return _writeTask.Wait(timeout);
    }

    private void Close()
    {
        Interlocked.Exchange(ref _closed, 1);
    }

    public async ValueTask DisposeAsync()
    {
        Close();
        await _writeTask.ConfigureAwait(false);
    }

    private async Task WriteAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Refuse to replace an existing run. Diagnostics failure cannot change file operations.
            await using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var header = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Type = "header", Schema = "filesmate-pr00/v1", RunId = _run, Pid = Environment.ProcessId,
                Frequency = Stopwatch.Frequency, Clock = "raw Stopwatch/QPC; common Windows boot time domain",
                StartedUtc = DateTimeOffset.UtcNow, Capacity = _capacity, MaxBytes = _maxBytes,
                Correlation = "pane + production generation + diagnostic operation; no new production version"
            });
            await WriteLineAsync(stream, header).ConfigureAwait(false);
            while (true)
            {
                while (TryDequeue(out var packet))
                {
                if (Volatile.Read(ref _truncated) != 0) continue;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Type = "event", RunId = _run, Pid = Environment.ProcessId, packet.Thread,
                    packet.Sequence, packet.Timestamp, Stage = packet.Stage.ToString(), packet.Pane,
                    packet.Generation, packet.Operation, packet.Count, Outcome = packet.Outcome.ToString(),
                    packet.DurationTicks, packet.Auxiliary
                });
                if (stream.Position + bytes.Length + 1 + FooterReserve > _maxBytes)
                {
                    Interlocked.Exchange(ref _truncated, 1);
                    continue;
                }
                await WriteLineAsync(stream, bytes).ConfigureAwait(false);
                Interlocked.Increment(ref _written);
                }
                if (Volatile.Read(ref _closed) != 0 && Volatile.Read(ref _activeWriters) == 0) break;
                // Flush only on the opt-in writer worker so a live collector can
                // inspect the latest complete numeric events without UI I/O.
                await stream.FlushAsync().ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false);
            }
            var footer = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Type = "summary", Accepted = Interlocked.Read(ref _accepted), Written = Interlocked.Read(ref _written),
                Dropped = Interlocked.Read(ref _dropped), AfterClose = Interlocked.Read(ref _afterClose),
                Truncated = Volatile.Read(ref _truncated) != 0, WriterErrors = Volatile.Read(ref _writerErrors),
                AllocatedBytesSinceTraceStart = GC.GetTotalAllocatedBytes(false) - _allocatedAtStart,
                Gen0Collections = GC.CollectionCount(0) - _gen0AtStart,
                Gen1Collections = GC.CollectionCount(1) - _gen1AtStart,
                Gen2Collections = GC.CollectionCount(2) - _gen2AtStart,
                GcPauseMilliseconds = (GC.GetTotalPauseDuration() - _pauseAtStart).TotalMilliseconds,
                LastGcHeapSizeBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
                Complete = Volatile.Read(ref _truncated) == 0 && Interlocked.Read(ref _dropped) == 0
                    && Interlocked.Read(ref _accepted) == Interlocked.Read(ref _written)
            });
            await WriteLineAsync(stream, footer).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException)
        {
            Interlocked.Increment(ref _writerErrors);
            Interlocked.Exchange(ref _closed, 1);
            // Missing header/footer is a collector-visible incomplete run, never a successful sample.
        }
    }

    private static async Task WriteLineAsync(Stream stream, byte[] bytes)
    {
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
    }
}
