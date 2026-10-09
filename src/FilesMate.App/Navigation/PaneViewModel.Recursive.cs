using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Core.Directories;
using FilesMate.Core.Diagnostics;
using System.Diagnostics;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.App.Navigation;

public sealed partial class PaneViewModel
{
    private RecursiveSession? _recursiveSession;
    private readonly SemaphoreSlim _recursivePreflightGate = new(1, 1);
    private CancellationTokenSource? _recursivePreflightCancellation;
    private EntrySort _ordinaryRecursiveSort = EntrySort.Name;
    private RecursiveProjection? _recursiveProjection;
    private bool _recursiveRequested;
    private bool _recursiveStale;
    private bool _recursiveTraversalStopped;
    private FileSystemWatcher? _recursiveWatcher;
    private long _recursiveEpoch;
    private long _recursiveSemanticVersion;
    private Task? _recursiveRecoveryTask;
    private bool _recursiveHoldingView;
    private int _recursiveNoticeQueued;
    private readonly RecursiveExpansionState _recursiveExpansionState = new();
    private FileAttributes _recursiveProjectionExcludedAttributes;
    private RecursiveBuild? _activeRecursiveBuild;
    private RecursiveBuild? _pendingRecursiveBuild;
    private Task _recursiveReleased = Task.CompletedTask;
    private CancellationTokenSource? _recursiveIdleCancellation;
    public Func<bool>? DeferRecursiveProgress { get; set; }

    private sealed record RecursiveBuild(RecursiveSession Session, RecursiveSnapshot Snapshot,
        EntrySort Sort, EntryFilter Filter, RecursiveExpansionPlan Expansion, RecursiveProjection? Reusable, IReadOnlySet<int>? TagIds,
        FileAttributes ExcludedAttributes, long SemanticVersion, bool UserInitiated, long Operation, EntryViewIndex? PreviousIndex, CancellationTokenSource Cancellation)
    {
        internal Task RunningTask { get; set; } = Task.CompletedTask;
        internal RecursiveProjection? PreviousProjection { get; init; }
    }

    public bool IsRecursive => _recursiveRequested;
    public EntrySort SortForPersistence => IsRecursive ? _ordinaryRecursiveSort : _sort;
    public string FilterForPersistence => _filter.Query;
    // The inexpensive UI gate excludes virtual/device/network roots. The worker
    // checks the actual drive type and root attributes before starting any scan.
    public bool CanUseRecursive => !_disposed && AddressText is { Length: >= 3 } path
        && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';

    public Task SetRecursiveEnabledAsync(bool enabled) => SetRecursiveEnabledCoreAsync(enabled, null, null);

    private async Task SetRecursiveEnabledCoreAsync(bool enabled, EntrySort? resumeSort, EntryFilter? resumeFilter, bool preserveView = false)
    {
        AssertUi();
        if (!enabled)
        {
            if (!IsRecursive) return;
            var beforeExit = _navigation.CurrentGeneration;
            EndRecursiveMode();
            if (!_disposed && !IsRecursive && beforeExit == _navigation.CurrentGeneration) RefreshSession(preserveSurface: false);
            return;
        }
        if (IsRecursive || _disposed) return;
        if (!CanUseRecursive) { ReportUserError(StringTable.Get("Recursive_Unsupported")); return; }
        _ordinaryRecursiveSort = _sort;
        _sort = RecursiveSnapshot.NormalizeTreeSort(resumeSort ?? _sort);
        if (resumeFilter is not null) _filter = resumeFilter;
        _recursiveRequested = true;
        var epoch = ++_recursiveEpoch;
        var root = AddressText;
        var generation = _navigation.CurrentGeneration;
        OnPropertyChanged(nameof(IsRecursive));
        if (_disposed || !IsRecursive || epoch != _recursiveEpoch || generation != _navigation.CurrentGeneration) return;
        OnPropertyChanged(nameof(Sort));
        if (_disposed || !IsRecursive || epoch != _recursiveEpoch || generation != _navigation.CurrentGeneration) return;
        if (resumeFilter is not null) OnPropertyChanged(nameof(FilterQuery));
        if (_disposed || !IsRecursive || epoch != _recursiveEpoch || generation != _navigation.CurrentGeneration) return;
        StatusText = StringTable.Get("Recursive_Checking");
        if (_disposed || !IsRecursive || epoch != _recursiveEpoch || generation != _navigation.CurrentGeneration) return;
        bool supported;
        var preflightCancellation = new CancellationTokenSource();
        _recursivePreflightCancellation = preflightCancellation;
        try
        {
            // A cancelled native probe may still be unwinding. Keep rapid
            // toggles bounded to one actual filesystem preflight per pane.
            await _recursivePreflightGate.WaitAsync(preflightCancellation.Token);
            try
            {
                // Do not accumulate retired scan/projection workers on rapid same-pane restarts.
                // This is an async wait; cancellation still leaves navigation and input responsive.
                await _recursiveReleased.WaitAsync(preflightCancellation.Token);
                supported = await Task.Run(() =>
                {
                    preflightCancellation.Token.ThrowIfCancellationRequested();
                    var drive = new DriveInfo(Path.GetPathRoot(root)!);
                    return drive.DriveType is DriveType.Fixed or DriveType.Removable
                        && (new RecursiveDirectoryProbe().GetAttributes(root, preflightCancellation.Token)
                            & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Offline)) == FileAttributes.Directory;
                });
            }
            finally { _recursivePreflightGate.Release(); }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { supported = false; }
        finally
        {
            if (ReferenceEquals(_recursivePreflightCancellation, preflightCancellation)) _recursivePreflightCancellation = null;
            preflightCancellation.Dispose();
        }
        if (_disposed || !_recursiveRequested || epoch != _recursiveEpoch
            || generation != _navigation.CurrentGeneration || !string.Equals(root, AddressText, StringComparison.OrdinalIgnoreCase)) return;
        if (!supported)
        {
            EndRecursiveMode();
            ReportUserError(StringTable.Get("Recursive_Unsupported"));
            return;
        }
        var intent = _navigation.Refresh();
        if (intent is null) { EndRecursiveMode(); return; }
        bool OwnsStart() => !_disposed && IsRecursive && epoch == _recursiveEpoch
            && _navigation.Allows(intent.Value.Generation);
        if (!OwnsStart()) return;
        CancelWatchRefresh();
        if (!OwnsStart()) return;
        ClearWatchQueue();
        CancelIndexBuild();
        if (!OwnsStart()) return;
        _releasedFolder = DisposeCapturedAsync();
        if (!OwnsStart()) return;
        if (!preserveView) { _viewIndex = null; _viewBinding = null; }
        _recursiveHoldingView = preserveView;
        PlaceholderNames = [];
        _recursiveStale = false;
        _recursiveTraversalStopped = false;
        var operationProbe = new WindowsRecursiveDirectoryProbe();
        var session = new RecursiveSession(_enumerator, intent.Value.PaneId, intent.Value.Generation, root,
            new RecursiveReadOptions
            {
                AllowContinuation = true,
                AutomaticContinuation = true,
                MemoryBudgetPool = Memory.RecursiveProcessBudget.Shared,
                InitialViewStateBytes = _recursiveViewRetainedBytes,
                // Keep finite history room inside the existing admission budget, including overlapping publications.
                MinimumViewStateReserveBytes = 1024 * 1024,
                RequireOperationCompatibilityVerification = true,
                DirectoryOptions = DirectoryReadOptions.Default with
                {
                    IncludeHidden = true,
                    IncludeSystem = true,
                    IncludeFolderItemCounts = false,
                    IncludeShortcutTargets = false,
                    IncludeHardLinkCounts = false,
                }
            }, directoryProbe: operationProbe, operationCompatibilityProbe: operationProbe);
        _recursiveSession = session;
        if (_expansionTargets.Count == 0)
            _recursiveExpansionState.SetDepthWithUndo(session.CurrentSnapshot, 0, 0);
        session.Changed += RecursiveChanged;
        _tagFilter = null;
        OnPropertyChanged(nameof(TagFilterLabel));
        if (!OwnsStart()) return;
        if (_sort.Column == EntrySortColumn.ShellProperty)
        {
            _sort = _sort with { Column = EntrySortColumn.Name, PropertyName = null };
            OnPropertyChanged(nameof(Sort));
            if (!OwnsStart()) return;
        }
        if (!preserveView) ItemCount = 0;
        if (!OwnsStart()) return;
        ErrorText = null;
        if (!OwnsStart()) return;
        IsLoading = true;
        if (!OwnsStart()) return;
        StartRecursiveWatchHints(root, session, operationProbe);
        if (!OwnsStart()) return;
        if (!preserveView) OnPropertyChanged(nameof(ViewIndex));
        if (!OwnsStart()) return;
        if (!preserveView) OnPropertyChanged(nameof(ViewBinding));
        if (!OwnsStart()) return;
        if (!preserveView) OnPropertyChanged(nameof(Store));
        if (!OwnsStart()) return;
        QueueRecursiveProjection(semanticChange: true);
        WhenCurrentSessionCompletes = RunRecursiveAsync(session);
        if (OwnsStart()) RaiseToolbar();
    }

    private async Task RunRecursiveAsync(RecursiveSession session)
    {
        try { await session.RunAsync().ConfigureAwait(false); }
        catch (Exception error) { App.LogFailure("RecursiveScan", error); }
        finally { RecursiveChanged(new(session.CurrentSnapshot)); }
    }

    private async Task RestartRecursiveAsync()
    {
        var generation = _navigation.CurrentGeneration;
        var path = AddressText;
        var recursiveSort = _sort;
        var recursiveFilter = _filter;
        EndRecursiveMode(preserveHistoryScope: true, preserveView: true);
        if (_disposed || IsRecursive || generation != _navigation.CurrentGeneration
            || !string.Equals(path, AddressText, StringComparison.OrdinalIgnoreCase)) return;
        await SetRecursiveEnabledCoreAsync(true, recursiveSort, recursiveFilter, preserveView: true);
    }

    public bool CancelRecursiveScan()
    {
        AssertUi();
        if (!IsRecursive) return false;
        var depthCancelled = CancelPendingDepth();
        if (_recursiveSession is not { } session)
        {
            EndRecursiveMode();
            RefreshSession(preserveSurface: false);
            return true;
        }
        if (session.State is not (RecursiveSessionState.Loading or RecursiveSessionState.Paused)) return depthCancelled;
        session.PauseTraversal();
        _recursiveTraversalStopped = true;
        QueueRecursiveProjection(semanticChange: true);
        return true;
    }

    public bool CanContinueRecursiveScan => !_disposed && !_recursiveTraversalStopped
        && _recursiveSession is { AutomaticContinuation: false, CurrentSnapshot: { State: RecursiveSessionState.Paused, Budget.CanContinue: true } };

    public void ContinueRecursiveScan()
    {
        AssertUi();
        if (!CanContinueRecursiveScan) return;
        if (_recursiveSession?.Continue() != true) { StatusText = StringTable.Get("Recursive_MemoryPressure"); return; }
        StatusText = StringTable.Get("Recursive_Continuing");
    }

    private bool RecursiveFilterRevealsAncestors => !string.IsNullOrEmpty(_filter.Query) || _filter.Metadata.IsActive
        || _filter.FilesOnly || _filter.DirectoriesOnly || _tagFilter is not null;

    public bool IsRecursiveNodeDesiredExpanded(int entryId) => _recursiveSession?.CurrentSnapshot is { } snapshot
        && entryId >= 0 && entryId <= snapshot.Count
        && (entryId == 0 || _recursiveProjection?.GetChildState(entryId, !_recursiveStale) != RecursiveChildState.Empty)
        && _recursiveExpansionState.IsExpanded(snapshot, entryId);

    public bool IsRecursiveSubtreeDesiredExpanded(int entryId) => _recursiveSession?.CurrentSnapshot is { } snapshot
        && entryId >= 0 && entryId <= snapshot.Count && _recursiveExpansionState.IsSubtreeExpanded(snapshot, entryId);

    public void ToggleRecursiveNode(int entryId)
    {
        AssertUi();
        // Chevron toggles use canonical desired state, never the asynchronously last-applied view.
        OpenRecursiveNode(entryId);
    }

    public void SetRecursiveExpanded(int entryId, bool expanded, bool subtree = false)
    {
        AssertUi();
        if (_recursiveHoldingView && entryId != 0) { StatusText = StringTable.Get("Recursive_Checking"); return; }
        CommitPendingDepth();
        if (FileOperationLifetime.IsBusy || App.FileUndo.IsApplying)
        { ReportUserError(StringTable.Get("Files_Busy")); return; }
        var operation = PerformanceTrace.NextOperation();
        PerformanceTrace.Record(PerformanceStage.RecursiveExpansionInput, generation: _navigation.CurrentGeneration,
            operation: operation, count: entryId, auxiliary: (expanded ? 1 : 0) | (subtree ? 2 : 0));
        if (_disposed || !IsRecursive || RecursiveFilterRevealsAncestors || _recursiveProjection is not { } projection
            || _recursiveSession?.CurrentSnapshot is not { } snapshot)
        {
            PerformanceTrace.Record(PerformanceStage.RecursiveExpansionRejected, generation: _navigation.CurrentGeneration,
                operation: operation, count: entryId, auxiliary: RecursiveFilterRevealsAncestors ? 1 : 2);
            return;
        }
        if (entryId >= 0 && entryId <= snapshot.Count)
            PerformanceTrace.Record(PerformanceStage.RecursiveExpansionNodeState, generation: snapshot.Generation,
                operation: operation, count: entryId, auxiliary: (long)(entryId == 0 ? snapshot.RootTraversalState : snapshot.GetNode(entryId).TraversalState)
                    | ((long)snapshot.State << 8) | (_recursiveStale ? 1L << 16 : 0) | (_recursiveTraversalStopped ? 1L << 17 : 0));
        if (entryId == 0) { if (!subtree) return; }
        else if (entryId < 1 || entryId > snapshot.Count || !projection.IsVisible(entryId)
            || !snapshot.GetNode(entryId).IsDirectory) return;
        else if (snapshot.GetNode(entryId).TraversalSkipped)
        { ReportUserError(StringTable.Get("Recursive_NotTraversed")); return; }
        if (expanded && entryId != 0 && projection.GetChildState(entryId, !_recursiveStale) == RecursiveChildState.Empty) return;
        var resumedTraversal = expanded && RequestRecursiveTraversal(entryId);
        if (!subtree && _recursiveExpansionState.IsExpanded(snapshot, entryId) == expanded)
        {
            if (expanded && _recursiveTraversalStopped) RecoverStoppedRecursiveScan();
            return;
        }
        if (!RecordRecursiveExpansion(snapshot, entryId, expanded, subtree)) return;
        PerformanceTrace.Record(PerformanceStage.RecursiveDesiredState, generation: snapshot.Generation,
            operation: operation, count: entryId, auxiliary: (expanded ? 1 : 0) | (subtree ? 2 : 0));
        // One batch intention and one build, even for a whole subtree. Later arrivals inherit its scope.
        // Apply the intention to the already sorted publication first. Scan
        // progress can rebuild a newer revision afterwards on the same worker.
        StatusText = StringTable.Get(expanded ? "Recursive_Expanding" : "Recursive_Collapsing");
        if (_disposed || !IsRecursive || _recursiveSession?.SessionId != snapshot.SessionId) return;
        QueueRecursiveProjection(semanticChange: true, operation: operation, preferPublishedSnapshot: true);
        // Expanding a captured tree is a view action. Structural watch hints
        // remain visible; only an explicit refresh replaces a stopped session.
        if (expanded && _recursiveTraversalStopped)
            RecoverStoppedRecursiveScan();
        else if (expanded && !resumedTraversal && snapshot.State == RecursiveSessionState.Paused)
        {
            if (_recursiveSession?.AutomaticContinuation == true && snapshot.Budget.CanContinue) return;
            if (CanContinueRecursiveScan) ContinueRecursiveScan();
            else ReportUserError(StringTable.Get("Recursive_MemoryPressure"));
        }
    }

    private void MarkRecursiveStale(bool stopTraversal = true)
    {
        if (!IsRecursive || _recursiveStale && (!stopTraversal || _recursiveTraversalStopped)) return;
        _recursiveStale = true;
        if (stopTraversal)
        {
            _recursiveTraversalStopped = true;
            _recursiveWatcher?.Dispose();
            _recursiveWatcher = null;
            _recursiveSession?.PauseTraversal();
        }
        // Metadata changes revoke operation safety but need not truncate this
        // read-only tree. Pending directories retain their native attribute and
        // reparse checks. Structural changes pause the retained stream. Explicit
        // expansion can resume read-only loading; operation validity stays revoked.
        QueueRecursiveProjection(semanticChange: true, explicitAction: false);
        StatusText = StringTable.Get("Recursive_Stale");
    }

    private bool RequestRecursiveTraversal(int entryId)
    {
        if (_recursiveSession?.RequestTraversal(entryId) != true) return false;
        var resumedTraversal = _recursiveTraversalStopped;
        _recursiveTraversalStopped = false;
        // Resuming reads never grants file-operation safety on a stale snapshot.
        if (_recursiveStale) StatusText = StringTable.Get("Recursive_Stale");
        else StatusText = StringTable.Get("Recursive_Continuing");
        return resumedTraversal;
    }

    private void RecoverStoppedRecursiveScan()
    {
        if (_disposed || !IsRecursive || !_recursiveTraversalStopped || _recursiveSession is not { } session
            || _recursiveRecoveryTask is { IsCompleted: false }) return;
        StatusText = StringTable.Get("Recursive_Checking");
        _recursiveRecoveryTask = RecoverStoppedRecursiveScanAsync(session);
    }

    private async Task RecoverStoppedRecursiveScanAsync(RecursiveSession expected)
    {
        // Coalesce repeated row/batch input before replacing the stopped session.
        await Task.Yield();
        if (_disposed || !IsRecursive || !_recursiveTraversalStopped
            || !ReferenceEquals(_recursiveSession, expected)) return;
        await RestartRecursiveAsync();
    }

    private void RecursiveChanged(RecursiveSessionChange change)
    {
        var session = _recursiveSession;
        if (session is null || session.SessionId != change.SessionId || _disposed) return;
        if (Interlocked.Exchange(ref _recursiveNoticeQueued, 1) != 0) return;
        _dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref _recursiveNoticeQueued, 0);
            // A queued callback may belong to an older session. Always consume
            // the newest current snapshot so its terminal notification cannot be lost.
            if (!_disposed && _recursiveSession is not null)
            {
                // A terminal scan notification must stop the indicator even
                // while pointer input defers a newer view projection.
                UpdateRecursiveScanActivity();
                QueueRecursiveProjection();
            }
        });
    }

    private void QueueRecursiveProjection(bool semanticChange = false, bool explicitAction = true, long operation = 0,
        bool preferPublishedSnapshot = false, bool keepPendingDepth = false)
    {
        if (semanticChange && !keepPendingDepth) CommitPendingDepth();
        if (_disposed || _recursiveSession is not { } session) return;
        // Retain the previous publication while a replacement capture is loading.
        // One terminal publication replaces it; never publish an empty recovery view.
        if (_recursiveHoldingView && (session.State == RecursiveSessionState.Loading
            || session.State == RecursiveSessionState.Paused && session.AutomaticContinuation && session.CurrentSnapshot.Budget.CanContinue)) return;
        if (semanticChange)
        {
            _recursiveSemanticVersion++;
            _activeRecursiveBuild?.Cancellation.Cancel();
        }
        var userInitiated = semanticChange && explicitAction || _pendingRecursiveBuild?.UserInitiated == true;
        if (operation == 0 && _pendingRecursiveBuild?.UserInitiated == true) operation = _pendingRecursiveBuild.Operation;
        _pendingRecursiveBuild?.Cancellation.Dispose();
        var snapshot = preferPublishedSnapshot && _recursiveProjection?.SessionId == session.SessionId
            ? _recursiveProjection.Snapshot : session.CurrentSnapshot;
        var excluded = ExplorerPreferenceBridge.ShowHiddenFiles() ? 0 : FileAttributes.Hidden | FileAttributes.System;
        // Exact snapshot reference includes revision, traversal state and compatibility observations.
        // Reuse is restricted to a matching non-filtered structural basis; all other semantics rebuild it.
        var reusable = _recursiveProjection is { IsFiltering: false } previous
            && ReferenceEquals(previous.Snapshot, snapshot) && previous.Index.Sort == _sort && previous.Index.Filter == _filter
            && _tagFilter is null && _recursiveProjectionExcludedAttributes == excluded ? previous : null;
        if (operation == 0) operation = PerformanceTrace.NextOperation();
        operation = AcceptanceOperation(operation);
        _pendingRecursiveBuild = new(session, snapshot, _sort, _filter,
            _recursiveExpansionState.Capture(), reusable, _tagFilter?.MatchingEntryIds, excluded,
            _recursiveSemanticVersion, userInitiated, operation, _recursiveProjection?.Index, new CancellationTokenSource())
            { PreviousProjection = _recursiveProjection };
        PerformanceTrace.Record(PerformanceStage.RecursiveProjectionQueued, generation: snapshot.Generation,
            operation: operation, count: snapshot.Count, auxiliary: reusable is null ? 0 : 1);
        StartPendingRecursiveBuild();
    }

    private void StartPendingRecursiveBuild()
    {
        if (_activeRecursiveBuild is not null || _pendingRecursiveBuild is not { } request) return;
        if (!request.UserInitiated && _recursiveProjection is not null && DeferRecursiveProgress?.Invoke() == true)
        {
            if (_recursiveIdleCancellation is null)
            {
                _recursiveIdleCancellation = new CancellationTokenSource();
                _ = ResumeRecursiveAfterInputAsync(_recursiveIdleCancellation);
            }
            return;
        }
        _pendingRecursiveBuild = null;
        RestoreRecursiveIntents(request.Snapshot);
        request = request with { Expansion = _recursiveExpansionState.Capture() };
        _activeRecursiveBuild = request;
        request.RunningTask = BuildRecursiveProjectionAsync(request);
    }

    private async Task ResumeRecursiveAfterInputAsync(CancellationTokenSource cancellation)
    {
        try { await Task.Delay(125, cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _dispatcher.Post(() =>
        {
            var owns = ReferenceEquals(_recursiveIdleCancellation, cancellation);
            if (owns) _recursiveIdleCancellation = null;
            var resume = owns && !cancellation.IsCancellationRequested && !_disposed;
            cancellation.Dispose();
            if (resume) StartPendingRecursiveBuild();
        });
    }

    private async Task BuildRecursiveProjectionAsync(RecursiveBuild request)
    {
        RecursiveProjection? projection = null;
        EntrySourcePrefixProof? prefixProof = null;
        RecursiveIdentityRebase? identityRebase = null;
        string? acceptanceDump = null;
        Exception? failure = null;
        try
        {
            projection = await Task.Run(() =>
            {
                var started = Stopwatch.GetTimestamp();
                PerformanceTrace.Record(PerformanceStage.RecursiveProjectionStarted, generation: request.Snapshot.Generation,
                    operation: request.Operation, count: request.Snapshot.Count, auxiliary: request.Reusable is null ? 0 : 1);
                var result = request.Reusable is not null ? request.Reusable.RebuildExpansion(request.Expansion, request.Cancellation.Token)
                    : request.Snapshot.BuildProjection(request.Sort, request.Filter, _names, cancellationToken: request.Cancellation.Token,
                        additionalMatch: request.TagIds is null ? null : entry => request.TagIds.Contains(entry.Id),
                        excludedAttributes: request.ExcludedAttributes, expansionPlan: request.Expansion);
                if (request.PreviousIndex is { } previousIndex)
                    prefixProof = result.Index.CompareSourcePrefix(previousIndex, request.Cancellation.Token, allowRangeReplacement: request.Reusable is not null);
                if (request.PreviousProjection is { } prior && prior.SessionId != result.SessionId)
                    identityRebase = RecursiveIdentityRebase.Create(prior.Snapshot, result.Snapshot, request.Cancellation.Token);
                PerformanceTrace.Record(PerformanceStage.RecursiveProjectionCompleted, generation: result.Generation,
                    operation: request.Operation, count: result.Index.Count, durationTicks: Stopwatch.GetTimestamp() - started,
                    auxiliary: request.Reusable is null ? 0 : 1);
                return result;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { failure = error; }
        var dispatchStarted = Stopwatch.GetTimestamp();
        PerformanceTrace.Record(PerformanceStage.RecursiveDispatcherQueued, generation: request.Snapshot.Generation, operation: request.Operation);
        _dispatcher.Post(() =>
        {
            PerformanceTrace.Record(PerformanceStage.RecursiveDispatcherEntered, generation: request.Snapshot.Generation,
                operation: request.Operation, durationTicks: Stopwatch.GetTimestamp() - dispatchStarted);
            var ownsRequest = ReferenceEquals(_activeRecursiveBuild, request);
            if (ownsRequest) _activeRecursiveBuild = null;
            if (ownsRequest && !_disposed && ReferenceEquals(_recursiveSession, request.Session)
                && _navigation.Allows(request.Session.Generation)
                && request.SemanticVersion == _recursiveSemanticVersion && !request.Cancellation.IsCancellationRequested)
            {
                if (projection is not null && (_recursiveProjection is null || projection.SessionId != _recursiveProjection.SessionId || projection.Revision >= _recursiveProjection.Revision))
                {
                    if (!request.UserInitiated && _recursiveProjection is not null && DeferRecursiveProgress?.Invoke() == true)
                        QueueRecursiveProjection();
                    else
                    {
                        _recursiveProjectionExcludedAttributes = request.ExcludedAttributes;
                        PublishRecursiveProjection(projection, request.Operation, prefixProof, identityRebase, acceptanceDump);
                        // A user expansion can replace an already queued final
                        // scan notice with its older published snapshot. Always
                        // catch up afterward; retain the user's latest expansion
                        // intent and never leave pending directories frozen.
                        var latest = request.Session.CurrentSnapshot;
                        if (!_disposed && ReferenceEquals(_recursiveSession, request.Session)
                            && projection.RequiresSnapshotCatchUp(latest)
                            && (_pendingRecursiveBuild is null || _pendingRecursiveBuild.Snapshot.Revision < latest.Revision))
                            QueueRecursiveProjection(explicitAction: false);
                    }
                }
                else if (failure is not null) { App.LogFailure("RecursiveProjection", failure); ReportUserError(failure.Message); }
            }
            request.Cancellation.Dispose();
            if (ownsRequest) StartPendingRecursiveBuild();
        });
    }

    private void UpdateRecursiveScanActivity()
    {
        var current = _recursiveSession?.CurrentSnapshot;
        IsLoading = current is not null && RecursiveScanActivity.IsActive(current.State,
            _recursiveSession!.AutomaticContinuation, current.Budget.CanContinue, _recursiveTraversalStopped);
    }

    private void PublishRecursiveProjection(RecursiveProjection projection, long operation, EntrySourcePrefixProof? prefixProof, RecursiveIdentityRebase? identityRebase = null, string? acceptanceDump = null)
    {
        CommitPendingDepth();
        var binding = new EntryViewBinding(projection.Store, projection.Index, projection.Generation,
            projection.Snapshot.RootPath, entry => projection.ResolvePath(entry.Id), projection,
            () => !_disposed && IsRecursive && !_recursiveStale && _recursiveSession?.SessionId == projection.SessionId,
            diagnosticOperation: operation, sourcePrefixProof: prefixProof) { RecursiveRebase = identityRebase };
        _recursiveHoldingView = false;
        _recursiveProjection = projection;
        _viewIndex = projection.Index;
        _viewBinding = binding;
        bool Owns() => !_disposed && IsRecursive && _navigation.Allows(projection.Generation)
            && _recursiveSession?.SessionId == projection.SessionId
            && ReferenceEquals(_recursiveProjection, projection) && ReferenceEquals(_viewBinding, binding);
        ItemCount = projection.Index.Count;
        if (!Owns()) return;
        UpdateRecursiveScanActivity();
        if (!Owns()) return;
        ErrorText = projection.State == RecursiveSessionState.Failed ? projection.Snapshot.Error?.Message : null;
        if (!Owns()) return;
        var snapshot = _recursiveSession?.CurrentSnapshot ?? projection.Snapshot;
        StatusText = _recursiveStale ? StringTable.Get("Recursive_Stale")
            : _recursiveTraversalStopped ? StringTable.Format("Recursive_Stopped", projection.MatchCount, snapshot.Count, snapshot.RootPath)
            : snapshot.State == RecursiveSessionState.Paused
                ? StringTable.Format(snapshot.Budget.CanContinue ? "Recursive_AutomaticPaused" : "Recursive_MemoryCeiling",
                    projection.MatchCount, snapshot.Count, snapshot.Budget.EstimatedRetainedBytes / (1024 * 1024), snapshot.RootPath)
            : snapshot.LimitReached ? StringTable.Format("Recursive_Limit", projection.MatchCount, snapshot.Count, snapshot.RootPath)
            : snapshot.State == RecursiveSessionState.Partial
                ? StringTable.Format("Recursive_Partial", projection.MatchCount, snapshot.Count, snapshot.SkippedDirectories + snapshot.SkippedEntries, snapshot.RootPath)
            : snapshot.State == RecursiveSessionState.Failed ? snapshot.Error?.Message ?? StringTable.Get("Recursive_Unsupported")
            : StringTable.Format(snapshot.State == RecursiveSessionState.Loading ? "Recursive_Loading"
                : snapshot.State == RecursiveSessionState.Cancelled ? "Recursive_Cancelled" : "Recursive_Completed",
                projection.MatchCount, snapshot.Count, snapshot.RootPath);
        if (!Owns()) return;
        OnPropertyChanged(nameof(ViewIndex));
        if (!Owns()) return;
        OnPropertyChanged(nameof(ViewBinding));
        if (!Owns()) return;
        OnPropertyChanged(nameof(Store));
        if (Owns()) CaptureRecursiveAcceptancePublication(projection, operation);
    }

    private void StartRecursiveWatchHints(string root, RecursiveSession session, WindowsRecursiveDirectoryProbe operationProbe)
    {
        // Notifications never mutate a recursive tree by name. Any changed
        // branch or overflow invalidates the captured snapshot; refresh is explicit.
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 16 * 1024,
            };
            var noticeQueued = 0;
            var stopQueued = 0;
            void Changed(bool stopTraversal = true)
            {
                if (stopTraversal) Interlocked.Exchange(ref stopQueued, 1);
                if (Interlocked.Exchange(ref noticeQueued, 1) != 0) return;
                _dispatcher.Post(() =>
                {
                    Interlocked.Exchange(ref noticeQueued, 0);
                    var stop = Interlocked.Exchange(ref stopQueued, 0) != 0;
                    if (!_disposed && ReferenceEquals(_recursiveSession, session)) MarkRecursiveStale(stop);
                });
            }
            watcher.Changed += (_, args) =>
            {
                // Read-only native directory queries can emit Changed on Windows.
                // Ignore only a verified identical directory identity + metadata + case
                // policy. Every file, unknown path, failure or meaningful change still
                // invalidates; Created/Deleted/Renamed/Error remain unconditional below.
                if (!operationProbe.IsUnchangedObservedDirectory(args.FullPath)) Changed(stopTraversal: false);
            };
            watcher.Created += (_, _) => Changed();
            watcher.Deleted += (_, _) => Changed();
            watcher.Renamed += (_, _) => Changed();
            watcher.Error += (_, _) => Changed();
            _recursiveWatcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _recursiveWatcher?.Dispose();
            _recursiveWatcher = null;
            App.LogFailure("RecursiveWatch", error);
            _recursiveStale = true;
        }
    }

    private void EndRecursiveMode(bool restoreOrdinary = true, bool preserveHistoryScope = false, bool preserveView = false)
    {
        if (!_recursiveRequested && _recursiveSession is null) return;
        CancelPendingDepth(queue: false);
        if (!preserveHistoryScope) ResetRecursiveHistoryScope();
        _recursiveRequested = false;
        ++_recursiveEpoch;
        ++_recursiveSemanticVersion;
        _recursivePreflightCancellation?.Cancel();
        _recursivePreflightCancellation = null;
        _recursiveIdleCancellation?.Cancel();
        _recursiveIdleCancellation = null;
        _pendingRecursiveBuild?.Cancellation.Dispose();
        _pendingRecursiveBuild = null;
        var retiringBuild = _activeRecursiveBuild?.RunningTask ?? Task.CompletedTask;
        _activeRecursiveBuild?.Cancellation.Cancel();
        _activeRecursiveBuild = null;
        if (_recursiveSession is { } session)
        {
            session.Changed -= RecursiveChanged;
            session.Cancel();
            _recursiveReleased = Task.WhenAll(_recursiveReleased, ReleaseRecursiveSessionAfterBuildAsync(session, retiringBuild));
            _recursiveSession = null;
        }
        _recursiveWatcher?.Dispose();
        _recursiveWatcher = null;
        if (!preserveView) { _recursiveProjection = null; _recursiveExpansionState.Clear(); }
        _recursiveHoldingView = preserveView;
        _recursiveStale = false;
        _recursiveTraversalStopped = false;
        if (restoreOrdinary)
        {
            _sort = _ordinaryRecursiveSort;
        }
        var exitEpoch = _recursiveEpoch;
        if (preserveView) return;
        OnPropertyChanged(nameof(IsRecursive));
        if (_disposed || IsRecursive || exitEpoch != _recursiveEpoch || !restoreOrdinary) return;
        OnPropertyChanged(nameof(Sort));
        if (_disposed || IsRecursive || exitEpoch != _recursiveEpoch) return;
        OnPropertyChanged(nameof(FilterQuery));
    }

    private static async Task ReleaseRecursiveSessionAfterBuildAsync(RecursiveSession session, Task retiringBuild)
    {
        try { await retiringBuild.ConfigureAwait(false); }
        catch (Exception error) { App.LogFailure("RetiringRecursiveProjection", error); }
        await session.DisposeAsync().ConfigureAwait(false);
    }
}
