using System.Diagnostics;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;

namespace FilesMate.Core.Recursive;

/// <summary>
/// One manually requested recursive scan. A single worker consumes one bounded directory stream at
/// a time, so consumer backpressure reaches the provider and directory work never fans out per file.
/// Queued directory identities are bounded by MaximumNodes. This session never starts a watcher.
/// Cancel suppresses publications and mutations synchronously; DisposeAsync waits for safe provider
/// cleanup, which cannot promise to interrupt an already blocked filesystem call immediately.
/// </summary>
public sealed class RecursiveSession : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IDirectoryEnumerator _enumerator;
    private readonly IRecursiveDirectoryProbe _probe;
    private readonly IRecursiveOperationCompatibilityProbe? _operationCompatibilityProbe;
    private readonly RecursiveReadOptions _options;
    private readonly EntryStore _store = new();
    private readonly List<RecursiveNode> _nodes = [];
    private readonly HashSet<int> _dirtyBlocks = [];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Stopwatch _progressClock = Stopwatch.StartNew();
    private CancellationTokenRegistration _externalCancellation;
    private RecursiveSnapshot _current;
    private Task? _run;
    private Task? _disposeTask;
    private bool _cancelled;
    private bool _disposed;
    private int _skippedDirectories;
    private int _skippedEntries;
    private RecursiveLimit _limits;
    private DirectoryReadError? _error;
    private Exception? _notificationError;
    private long _revision;
    private RecursiveTraversalState _rootTraversalState = RecursiveTraversalState.Pending;
    private bool _rootListingOmittedEntries;
    private RecursiveOperationCompatibility _rootOperationCompatibility;
    private int _nodeAllowance;
    private long _byteAllowance;
    private long _estimatedRetainedBytes;
    private TaskCompletionSource? _resume;
    private bool _canContinue;
    private int _pendingDirectories;
    private long _nameStringBytes, _viewBytes;
    private RecursiveMemoryEstimate _memoryEstimate, _nextMemoryEstimate;
    private bool _sharedPressure;
    private TaskCompletionSource? _traversalResume;
    private int _priorityDirectory = -1;

    /// <summary>Stop consuming the retained stream without destroying its cursor or frontier.</summary>
    public bool PauseTraversal()
    {
        lock (_gate)
        {
            if (_cancelled || _disposed || _current.State is not (RecursiveSessionState.Loading or RecursiveSessionState.Paused)) return false;
            _traversalResume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _current, _current.WithState(RecursiveSessionState.Paused, ++_revision));
            return true;
        }
    }

    /// <summary>Explicit view input resumes read-only enumeration and prioritizes an unread directory.</summary>
    public bool RequestTraversal(int entryId)
    {
        lock (_gate)
        {
            if (_cancelled || _disposed || entryId < 0 || entryId > _nodes.Count
                || entryId != 0 && !_nodes[entryId - 1].IsDirectory) return false;
            if (_current.State is not (RecursiveSessionState.Loading or RecursiveSessionState.Paused)) return false;
            _priorityDirectory = entryId;
            var resume = _traversalResume;
            _traversalResume = null;
            resume?.TrySetResult();
            return true;
        }
    }

    private async Task WaitForTraversalAsync(CancellationToken token, string? activePath = null)
    {
        Task? resume;
        lock (_gate) resume = _traversalResume?.Task;
        if (resume is null) return;
        Publish(RecursiveSessionState.Paused, force: true);
        await resume.WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (activePath is not null)
        {
            var attributes = _probe.GetAttributes(activePath, token);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline)) != FileAttributes.Directory)
                throw new IOException("The paused directory is no longer available for recursive traversal.");
        }
        Publish(RecursiveSessionState.Loading, force: true);
    }

    private bool TryDequeueDirectory(Queue<int> directories, out int parent)
    {
        int priority;
        lock (_gate) { priority = _priorityDirectory; _priorityDirectory = -1; }
        // Rotate only on explicit input, on the worker. No extra frontier or UI-thread walk.
        if (priority >= 0 && directories.Contains(priority))
            while (directories.Peek() != priority) directories.Enqueue(directories.Dequeue());
        return directories.TryDequeue(out parent);
    }

    public RecursiveSession(IDirectoryEnumerator enumerator, PaneId paneId, long generation,
        string rootPath, RecursiveReadOptions? options = null, IRecursiveDirectoryProbe? directoryProbe = null,
        IRecursiveOperationCompatibilityProbe? operationCompatibilityProbe = null)
    {
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        RecursivePath.ValidateLocalRoot(rootPath);
        _enumerator = enumerator;
        _probe = directoryProbe ?? new RecursiveDirectoryProbe();
        _operationCompatibilityProbe = operationCompatibilityProbe;
        _options = options ?? new RecursiveReadOptions();
        _options.Validate();
        _nodeAllowance = _options.MaximumNodes;
        _byteAllowance = _options.InitialRetainedByteAllowance;
        _viewBytes = _options.InitialViewStateBytes;
        _rootOperationCompatibility = _options.RequireOperationCompatibilityVerification
            ? RecursiveOperationCompatibility.Pending : RecursiveOperationCompatibility.NotRequired;
        PaneId = paneId;
        Generation = generation;
        RootPath = rootPath;
        SessionId = Guid.NewGuid();
        _current = new(SessionId, Generation, 0, RootPath, _store, _store.CaptureSnapshot(), [],
            RecursiveSessionState.Loading, 0, 0, RecursiveLimit.None, null,
            _rootTraversalState, false, CompleteEnumerationScope, _rootOperationCompatibility);
    }

    public Guid SessionId { get; }
    public PaneId PaneId { get; }
    public long Generation { get; }
    public string RootPath { get; }
    public RecursiveSnapshot CurrentSnapshot => Volatile.Read(ref _current);
    public RecursiveSessionState State => CurrentSnapshot.State;
    public bool AutomaticContinuation => _options.AutomaticContinuation;
    private long MaximumMemoryAllowance => Math.Min(_options.MaximumRetainedByteAllowance,
        _options.MemoryBudgetPool?.MaximumBytes ?? long.MaxValue);
    private bool CompleteEnumerationScope => _options.DirectoryOptions.IncludeHidden && _options.DirectoryOptions.IncludeSystem;
    /// <summary>First observer failure, if any. UI subscriber failures never turn a valid scan into a failed tree.</summary>
    public Exception? NotificationError => Volatile.Read(ref _notificationError);

    /// <summary>
    /// Coalesced worker-thread notifications. Handlers must be brief and must not wait for RunAsync.
    /// Observer exceptions are isolated and retained in NotificationError for diagnostics.
    /// </summary>
    public event Action<RecursiveSessionChange>? Changed;

    /// <summary>Explicitly admit one finite step. The suspended enumerator retains its exact cursor.</summary>
    public bool Continue()
    {
        lock (_gate)
        {
            if (_cancelled || _disposed || _current.State != RecursiveSessionState.Paused || _resume is null || !_canContinue) return false;
            var next = _nextMemoryEstimate with { ViewStateReserveBytes = ViewStateReserve(_viewBytes) };
            if (next.TotalBytes > MaximumMemoryAllowance || _options.MemoryBudgetPool?.TryReserve(SessionId, next.TotalBytes) == false) return false;
            _nodeAllowance = (int)Math.Min(int.MaxValue - 1L, (long)_nodeAllowance + _options.MaximumNodes);
            _byteAllowance = Math.Min(_options.MaximumRetainedByteAllowance, _byteAllowance + _options.RetainedByteStep);
            _limits &= ~(RecursiveLimit.Nodes | RecursiveLimit.MemoryBudget);
            var resume = _resume;
            _resume = null;
            _canContinue = false;
            _sharedPressure = false;
            resume.TrySetResult();
            return true;
        }
    }

    /// <summary>Reserve retained view-history paths before adding a new target. Failure never mutates history or expansion.</summary>
    public bool TryReserveViewState(long additionalBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(additionalBytes);
        lock (_gate)
        {
            // Stopped results remain browsable; view state does not resume filesystem work.
            if (_disposed) return false;
            if (additionalBytes > MaximumMemoryAllowance / 3 - _viewBytes) return false;
            var next = _memoryEstimate with { ViewStateReserveBytes = ViewStateReserve(_viewBytes + additionalBytes) };
            if (next.TotalBytes > _byteAllowance || _options.MemoryBudgetPool?.TryReserve(SessionId, next.TotalBytes) == false) return false;
            _viewBytes += additionalBytes;
            _memoryEstimate = next;
            _estimatedRetainedBytes = next.TotalBytes;
            return true;
        }
    }

    /// <summary>Idempotent start; even a synchronous provider is always consumed on a background worker.</summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) return _run;
            _externalCancellation = cancellationToken.Register(Cancel);
            return _run = Task.Run(ScanAsync, CancellationToken.None);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_cancelled || _disposed) return;
            _cancelled = true;
            if (_current.State is RecursiveSessionState.Loading or RecursiveSessionState.Paused)
                Volatile.Write(ref _current, _current.WithState(RecursiveSessionState.Cancelled, ++_revision));
            // Mark cancellation immediately; callback completion is deferred rather than blocking the UI.
            _ = _cancellation.CancelAsync();
        }
    }

    public void Dispose()
    {
        // Disposal starts cleanup without synchronously waiting on native I/O. Observe/await DisposeAsync
        // when shutdown needs a guarantee that the underlying enumerator and CTS have been released.
        _ = DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            Cancel();
            _disposed = true;
            Changed = null;
            return new(_disposeTask = CleanupAsync(_run));
        }
    }

    private async Task CleanupAsync(Task? run)
    {
        try { if (run is not null) await run.ConfigureAwait(false); }
        finally
        {
            await _externalCancellation.DisposeAsync().ConfigureAwait(false);
            _cancellation.Dispose();
            _options.MemoryBudgetPool?.Release(SessionId);
        }
    }

    private async Task WaitForContinuationAsync(Task resume, CancellationToken token)
    {
        if (_options.AutomaticContinuation)
        {
            // Resume the exact retained batch/frontier without a UI callback or a second enumerator.
            // A denied reservation never spins or blocks input. Disposal/cancel still wakes this wait.
            while (!resume.IsCompleted && CurrentSnapshot.Budget.CanContinue)
            {
                token.ThrowIfCancellationRequested();
                if (Continue()) break;
                await Task.WhenAny(resume, Task.Delay(TimeSpan.FromSeconds(1), token)).ConfigureAwait(false);
            }
        }
        // At the finite retained-data ceiling keep partial results and the cursor, reporting Paused.
        // This is admission accounting, not a process-memory cap or successful full enumeration.
        await resume.WaitAsync(token).ConfigureAwait(false);
    }

    private async Task ScanAsync()
    {
        var token = _cancellation.Token;
        var directories = new Queue<int>();
        directories.Enqueue(0);
        var rootFailed = false;
        try
        {
            var initialEstimate = EstimateMemory(0, 0, 0, 0, directories.EnsureCapacity(0), RootPath.Length);
            _memoryEstimate = initialEstimate;
            _estimatedRetainedBytes = initialEstimate.TotalBytes;
            while (_options.AllowContinuation && (initialEstimate.TotalBytes > _byteAllowance
                || _options.MemoryBudgetPool?.TryReserve(SessionId, initialEstimate.TotalBytes) == false))
            {
                Task resume;
                lock (_gate)
                {
                    ThrowIfStopped(token);
                    _limits |= RecursiveLimit.MemoryBudget;
                    _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _nextMemoryEstimate = initialEstimate;
                    _canContinue = initialEstimate.TotalBytes <= MaximumMemoryAllowance;
                    _pendingDirectories = directories.Count;
                    _sharedPressure = initialEstimate.TotalBytes <= _byteAllowance;
                    resume = _resume.Task;
                }
                Publish(RecursiveSessionState.Paused, force: true);
                await WaitForContinuationAsync(resume, token).ConfigureAwait(false);
            }
            while (directories.Count != 0)
            {
                await WaitForTraversalAsync(token).ConfigureAwait(false);
                if (!TryDequeueDirectory(directories, out var parent)) break;
                _pendingDirectories = directories.Count + 1;
                token.ThrowIfCancellationRequested();
                var path = ResolveLivePath(parent);
                var branchFailed = false;
                var receivedFinal = false;
                try
                {
                    var attributes = _probe.GetAttributes(path, token);
                    token.ThrowIfCancellationRequested();
                    var skip = (attributes & FileAttributes.ReparsePoint) != 0
                        ? RecursiveTraversalState.SkippedReparsePoint
                        : (attributes & FileAttributes.Offline) != 0 ? RecursiveTraversalState.SkippedOffline
                        : (attributes & FileAttributes.Directory) == 0 ? RecursiveTraversalState.Failed
                        : RecursiveTraversalState.Enumerating;
                    if (skip != RecursiveTraversalState.Enumerating)
                    {
                        lock (_gate)
                        {
                            ThrowIfStopped(token);
                            SetTraversal(parent, skip);
                            _skippedDirectories++;
                            _error ??= new(DirectoryReadErrorKind.UnsupportedLocation, 0,
                                "A directory is unavailable or excluded by the recursive traversal policy.", parent == 0);
                        }
                        if (parent == 0) { rootFailed = true; break; }
                        Publish(RecursiveSessionState.Loading, force: false);
                        continue;
                    }
                    var compatibility = CheckOperationCompatibility(path, token);
                    lock (_gate)
                    {
                        ThrowIfStopped(token);
                        SetOperationCompatibility(parent, compatibility);
                        SetTraversal(parent, RecursiveTraversalState.Enumerating);
                    }
                    // Do not turn user metadata/thumbnail preferences into eager work for an entire tree.
                    var readOptions = _options.DirectoryOptions with
                    {
                        IncludeFolderItemCounts = false, IncludeShortcutTargets = false, IncludeHardLinkCounts = false,
                    };
                    var request = new DirectoryRequest(PaneId, Generation, path, readOptions);
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    var foldedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    await foreach (var batch in _enumerator.EnumerateAsync(request, token).ConfigureAwait(false))
                    {
                        await WaitForTraversalAsync(token, path).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (batch.PaneId != PaneId || batch.Generation != Generation || !RecursivePath.Equals(path, batch.DirectoryPath))
                            continue;
                        var entryOffset = 0;
                        do
                        {
                            await WaitForTraversalAsync(token, path).ConfigureAwait(false);
                            Task? resume;
                            lock (_gate)
                            {
                                resume = AcceptBatchChunk(batch, ref entryOffset, names, foldedNames,
                                    directories, parent, path, token, ref branchFailed);
                            }
                            if (resume is null) break;
                            Publish(RecursiveSessionState.Paused, force: true);
                            await WaitForContinuationAsync(resume, token).ConfigureAwait(false);
                            token.ThrowIfCancellationRequested();
                            // Recheck mutable attributes before consuming the suspended stream.
                            // Application watch invalidation also cancels this session.
                            var resumedAttributes = _probe.GetAttributes(path, token);
                            if ((resumedAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline))
                                != FileAttributes.Directory)
                                throw new IOException("The paused directory is no longer available for recursive traversal.");
                            var resumedCompatibility = CheckOperationCompatibility(path, token);
                            lock (_gate)
                            {
                                ThrowIfStopped(token);
                                SetOperationCompatibility(parent, resumedCompatibility);
                            }
                        } while (entryOffset < batch.Entries.Count);
                        _pendingDirectories = directories.Count + 1;
                        Publish(RecursiveSessionState.Loading, force: false);
                        if (batch.IsFinal) receivedFinal = true;
                        if (batch.IsFinal || batch.Error?.IsTerminal == true || (_limits & RecursiveLimit.Nodes) != 0) break;
                    }
                    if (!receivedFinal && (_limits & RecursiveLimit.Nodes) == 0)
                    {
                        branchFailed = true;
                        _error ??= new(DirectoryReadErrorKind.PartialEnumeration, 0,
                            "The directory stream ended without its completion marker.", false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
                    or ArgumentException or NotSupportedException)
                {
                    branchFailed = true;
                    _error = ToReadError(error, parent == 0);
                }
                lock (_gate)
                {
                    ThrowIfStopped(token);
                    SetTraversal(parent, branchFailed ? RecursiveTraversalState.Failed
                        : (_limits & RecursiveLimit.Nodes) != 0 ? RecursiveTraversalState.SkippedNodeLimit : RecursiveTraversalState.Completed);
                    if (branchFailed) _skippedDirectories++;
                }
                if (parent == 0 && branchFailed) { rootFailed = true; break; }
                if ((_limits & RecursiveLimit.Nodes) != 0)
                {
                    lock (_gate)
                    {
                        ThrowIfStopped(token);
                        _skippedDirectories++;
                        foreach (var pending in directories)
                        {
                            SetTraversal(pending, RecursiveTraversalState.SkippedNodeLimit);
                            _skippedDirectories++;
                        }
                    }
                    break;
                }
                Publish(RecursiveSessionState.Loading, force: false);
            }
            if (rootFailed)
                lock (_gate) { ThrowIfStopped(token); MarkUnvisitedFailed(); }
            await WaitForTraversalAsync(token).ConfigureAwait(false);
            _pendingDirectories = directories.Count;
            _canContinue = false;
            _sharedPressure = false;
            Publish(rootFailed ? RecursiveSessionState.Failed
                : _skippedDirectories > 0 || _skippedEntries > 0 || _limits != RecursiveLimit.None
                    ? RecursiveSessionState.Partial : RecursiveSessionState.Completed, force: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { Cancel(); }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (_cancelled || _disposed) return;
                _error = ToReadError(error, true);
                MarkUnvisitedFailed();
            }
            Publish(RecursiveSessionState.Failed, force: true);
        }
    }

    // Keep this bounded CPU loop outside the async state machine. Constants are captured
    // while _gate is held; mutable capacities refresh immediately after actual growth.
    private Task? AcceptBatchChunk(DirectoryBatch batch, ref int entryOffset, HashSet<string> names,
        Dictionary<string, int> foldedNames, Queue<int> directories, int parent, string path,
        CancellationToken token, ref bool branchFailed)
    {
        ThrowIfStopped(token);
        Task? resume = null;
        var accepted = new List<FileEntryCore>(Math.Min(batch.Entries.Count - entryOffset,
            Math.Max(0, _nodeAllowance - _nodes.Count)));
        var windowsNames = RecursivePath.IsWindows(RootPath);
        var providerReserve = RecursiveMemoryEstimate.ProviderReserve(Math.Max(RootPath.Length, path.Length),
            _options.DirectoryOptions.BatchSize, _options.MaximumDepth);
        var viewReserve = ViewStateReserve(_viewBytes);
        var observationMemory = _probe as IRecursiveObservationMemory
            ?? _operationCompatibilityProbe as IRecursiveObservationMemory;
        var nodeCapacity = _nodes.Capacity;
        var storeCapacity = _store.AllocatedEntryCapacity; // Append only after the chunk.
        var namesCapacity = names.EnsureCapacity(0);
        var foldedCapacity = foldedNames.EnsureCapacity(0);
        var frontierCapacity = directories.EnsureCapacity(0);
        for (; entryOffset < batch.Entries.Count; entryOffset++)
        {
            token.ThrowIfCancellationRequested();
            var entry = batch.Entries[entryOffset];
            var stringBytes = (24L + 2L * entry.Name.Length + 7) & ~7L;
            if (_nodes.Count > nodeCapacity) nodeCapacity = _nodes.Capacity;
            if (names.Count > namesCapacity) namesCapacity = names.EnsureCapacity(0);
            if (foldedNames.Count > foldedCapacity) foldedCapacity = foldedNames.EnsureCapacity(0);
            if (directories.Count > frontierCapacity) frontierCapacity = directories.EnsureCapacity(0);
            var nextCount = _nodes.Count + 1;
            var nextEstimate = RecursiveMemoryEstimate.CreateForBatch(nextCount, _nameStringBytes + stringBytes,
                RecursiveMemoryEstimate.ListCapacityFor(nodeCapacity, nextCount),
                RecursiveMemoryEstimate.ListCapacityFor(storeCapacity, nextCount),
                RecursiveMemoryEstimate.HashCapacityFor(namesCapacity, names.Count + 1),
                RecursiveMemoryEstimate.HashCapacityFor(foldedCapacity, foldedNames.Count + 1),
                RecursiveMemoryEstimate.ListCapacityFor(frontierCapacity, directories.Count + 1),
                observationMemory?.ObservationByteLimit ?? 0, providerReserve, viewReserve);
            var nextBytes = nextEstimate.TotalBytes;
            var nodeBudget = _nodes.Count >= _nodeAllowance;
            var byteBudget = _options.AllowContinuation && nextBytes > _byteAllowance;
            var sharedBudget = _options.AllowContinuation && !nodeBudget && !byteBudget
                && _options.MemoryBudgetPool?.TryReserve(SessionId, nextBytes) == false;
            if (nodeBudget || byteBudget || sharedBudget)
            {
                if (nodeBudget) _limits |= RecursiveLimit.Nodes;
                if (byteBudget || sharedBudget) _limits |= RecursiveLimit.MemoryBudget;
                if (_options.AllowContinuation)
                {
                    _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _canContinue = _nodes.Count < int.MaxValue - 1
                        && nextBytes <= MaximumMemoryAllowance
                        && (_nodeAllowance < int.MaxValue - 1 || !nodeBudget);
                    _nextMemoryEstimate = nextEstimate;
                    _sharedPressure = sharedBudget;
                    _pendingDirectories = directories.Count + 1;
                    resume = _resume.Task;
                }
                break;
            }
            if (!RecursivePath.IsSafeName(entry.Name, windowsNames) || !names.Add(entry.Name))
            {
                _skippedEntries++;
                MarkListingOmitted(parent);
                _error ??= new(DirectoryReadErrorKind.PartialEnumeration, 0,
                    "A malformed or repeated directory entry was skipped.", false);
                continue;
            }
            var id = _nodes.Count + 1;
            var observation = RecursiveNodeObservation.None;
            if (foldedNames.TryGetValue(entry.Name, out var conflictingId))
            {
                MarkObservation(conflictingId, RecursiveNodeObservation.CaseFoldCollision);
                observation = RecursiveNodeObservation.CaseFoldCollision;
            }
            else foldedNames.Add(entry.Name, id);
            var depth = parent == 0 ? 0 : _nodes[parent - 1].Depth + 1;
            var directory = entry.Kind == EntryKind.Directory;
            var reparse = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            var traversal = !directory ? RecursiveTraversalState.NotDirectory
                : reparse ? RecursiveTraversalState.SkippedReparsePoint
                : (entry.Attributes & FileAttributes.Offline) != 0 ? RecursiveTraversalState.SkippedOffline
                : depth + 1 >= _options.MaximumDepth ? RecursiveTraversalState.SkippedDepthLimit
                : RecursiveTraversalState.Pending;
            _nodes.Add(new(id, parent, depth, directory, reparse, traversal)
            {
                Observation = observation,
                OperationCompatibility = directory && _options.RequireOperationCompatibilityVerification
                    ? RecursiveOperationCompatibility.Pending : RecursiveOperationCompatibility.NotRequired,
            });
            _dirtyBlocks.Add((id - 1) >> RecursiveSnapshot.BlockShift);
            accepted.Add(entry with { Id = id });
            if (traversal == RecursiveTraversalState.Pending) directories.Enqueue(id);
            else if (directory)
            {
                _skippedDirectories++;
                if (traversal == RecursiveTraversalState.SkippedDepthLimit) _limits |= RecursiveLimit.Depth;
            }
            _nameStringBytes += stringBytes;
            _memoryEstimate = nextEstimate;
            _estimatedRetainedBytes = nextBytes;
        }
        // Commit all admitted identities before publishing a pause, including split batches.
        _store.Append(accepted);
        if (resume is null && batch.Error is not null)
        {
            _error = batch.Error;
            branchFailed = true;
        }
        return resume;
    }

    private RecursiveMemoryEstimate EstimateMemory(int count, long nameBytes, int namesCapacity, int foldedCapacity,
        int frontierCapacity, int pathLength) => RecursiveMemoryEstimate.Create(count, nameBytes,
        RecursiveMemoryEstimate.ListCapacityFor(_nodes.Capacity, count),
        RecursiveMemoryEstimate.ListCapacityFor(_store.AllocatedEntryCapacity, count), namesCapacity,
        foldedCapacity, frontierCapacity, (_probe as IRecursiveObservationMemory)?.ObservationByteLimit
            ?? (_operationCompatibilityProbe as IRecursiveObservationMemory)?.ObservationByteLimit ?? 0,
        Math.Max(RootPath.Length, pathLength), _options.DirectoryOptions.BatchSize, _options.MaximumDepth, _viewBytes)
        with { ViewStateReserveBytes = ViewStateReserve(_viewBytes) };

    private long ViewStateReserve(long retainedBytes) => Math.Max(_options.MinimumViewStateReserveBytes, 3 * retainedBytes);

    private void Publish(RecursiveSessionState state, bool force)
    {
        // Show the first useful batch promptly even if a later directory blocks. Subsequent
        // publications are coalesced; a fast complete scan need not pay for per-batch snapshots.
        if (!force && _progressClock.Elapsed < _options.ProgressInterval && (_current.Count > 0 || _nodes.Count == 0)) return;
        lock (_gate)
        {
            if (_cancelled || _disposed) return;
            if (_traversalResume is not null && state == RecursiveSessionState.Loading)
                state = RecursiveSessionState.Paused;
            var token = _cancellation.Token;
            var nodes = RecursiveSnapshot.CaptureNodes(_nodes, _current, _dirtyBlocks, token);
            var entries = _store.CaptureSnapshot(token);
            var shared = _options.MemoryBudgetPool?.Statistics ?? default;
            var snapshot = new RecursiveSnapshot(SessionId, Generation, ++_revision, RootPath, _store, entries,
                nodes, state, _skippedDirectories, _skippedEntries, _limits, _error,
                _rootTraversalState, _rootListingOmittedEntries, CompleteEnumerationScope, _rootOperationCompatibility)
            {
                Budget = new(_nodeAllowance, _estimatedRetainedBytes, _byteAllowance,
                    _options.MaximumRetainedByteAllowance, _pendingDirectories, _canContinue)
                {
                    Estimate = _memoryEstimate,
                    SharedReservedBytes = shared.ReservedBytes,
                    SharedCapacityBytes = shared.CapacityBytes,
                    SharedPressure = _sharedPressure,
                    EstimatedObservationBytes = (_probe as IRecursiveObservationMemory)?.EstimatedObservationBytes
                        ?? (_operationCompatibilityProbe as IRecursiveObservationMemory)?.EstimatedObservationBytes ?? 0,
                },
            };
            _dirtyBlocks.Clear();
            Volatile.Write(ref _current, snapshot);
            _progressClock.Restart();
            // Serialized with Cancel: once Cancel returns, no later callback can start.
            var subscribers = Changed?.GetInvocationList();
            if (subscribers is not null)
                foreach (Action<RecursiveSessionChange> subscriber in subscribers)
                {
                    if (_cancelled || _disposed) break;
                    try { subscriber(new(snapshot)); }
                    catch (Exception error) { Interlocked.CompareExchange(ref _notificationError, error, null); }
                }
        }
    }

    private void SetTraversal(int id, RecursiveTraversalState state)
    {
        if (id == 0) { _rootTraversalState = state; return; }
        _nodes[id - 1] = _nodes[id - 1] with { TraversalState = state,
            IsReparsePoint = _nodes[id - 1].IsReparsePoint || state == RecursiveTraversalState.SkippedReparsePoint };
        _dirtyBlocks.Add((id - 1) >> RecursiveSnapshot.BlockShift);
    }

    private void MarkUnvisitedFailed()
    {
        if (_rootTraversalState is RecursiveTraversalState.Pending or RecursiveTraversalState.Enumerating)
            _rootTraversalState = RecursiveTraversalState.Failed;
        for (var i = 0; i < _nodes.Count; i++)
            if (_nodes[i].TraversalState is RecursiveTraversalState.Pending or RecursiveTraversalState.Enumerating)
            {
                SetTraversal(i + 1, RecursiveTraversalState.Failed);
                _skippedDirectories++;
            }
    }

    private void MarkListingOmitted(int id)
    {
        if (id == 0) _rootListingOmittedEntries = true;
        else MarkObservation(id, RecursiveNodeObservation.ListingOmittedEntries);
    }

    private void MarkObservation(int id, RecursiveNodeObservation observation)
    {
        _nodes[id - 1] = _nodes[id - 1] with { Observation = _nodes[id - 1].Observation | observation };
        _dirtyBlocks.Add((id - 1) >> RecursiveSnapshot.BlockShift);
    }

    private RecursiveOperationCompatibility CheckOperationCompatibility(string path, CancellationToken cancellationToken)
    {
        if (!_options.RequireOperationCompatibilityVerification) return RecursiveOperationCompatibility.NotRequired;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return _operationCompatibilityProbe?.IsLegacyOperationCompatible(path, cancellationToken) == true
                ? RecursiveOperationCompatibility.Compatible : RecursiveOperationCompatibility.Unverified;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return RecursiveOperationCompatibility.Unverified; }
    }

    private void SetOperationCompatibility(int id, RecursiveOperationCompatibility compatibility)
    {
        if (id == 0) { _rootOperationCompatibility = compatibility; return; }
        _nodes[id - 1] = _nodes[id - 1] with { OperationCompatibility = compatibility };
        _dirtyBlocks.Add((id - 1) >> RecursiveSnapshot.BlockShift);
    }

    private string ResolveLivePath(int id)
    {
        if (id == 0) return RootPath;
        var node = _nodes[id - 1];
        var names = new string[node.Depth + 1];
        for (var i = names.Length - 1; i >= 0; i--)
        {
            names[i] = _store[node.Id - 1].Name;
            if (node.ParentId != 0) node = _nodes[node.ParentId - 1];
        }
        return RecursivePath.Combine(RootPath, string.Join(RecursivePath.Separator(RootPath), names));
    }

    private void ThrowIfStopped(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_cancelled || _disposed) throw new OperationCanceledException(token);
    }

    private static DirectoryReadError ToReadError(Exception error, bool root) => new(error switch
    {
        UnauthorizedAccessException or System.Security.SecurityException => DirectoryReadErrorKind.AccessDenied,
        DirectoryNotFoundException or FileNotFoundException => DirectoryReadErrorKind.NotFound,
        ArgumentException or NotSupportedException => DirectoryReadErrorKind.PathInvalid,
        _ => DirectoryReadErrorKind.Unknown,
    }, 0, error.Message, root);
}
