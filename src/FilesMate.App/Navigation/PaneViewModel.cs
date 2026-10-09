using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Core.Directories;
using FilesMate.Core.Recursive;
using FilesMate.Core.Diagnostics;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;
using FilesMate.Platform.Windows.Shell;

namespace FilesMate.App.Navigation;

/// <summary>
/// Session state for one pane. Does not wrap each file in a view-model.
/// Thread-safety: public members except construction run on the UI dispatcher in the app.
/// Cancellation: a newer navigation disposes the previous <see cref="DirectorySession"/>.
/// </summary>
public sealed partial class PaneViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    public const int PlaceholderCap = 128;

    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IPathService _paths;
    private readonly IDirectoryEnumerator _enumerator;
    private readonly IDirectoryWatcher? _watcher;
    private readonly IComparer<string> _names;
    private readonly NavigationController _navigation;
    private readonly object _sessionGate = new();
    private readonly object _indexBuildGate = new();
    // One owner performs snapshot/key/sort work; only the newest waiting request is retained.
    private IndexBuildRequest? _activeIndexRequest;
    private IndexBuildRequest? _pendingIndexRequest;
    private readonly object _publicationGate = new();
    private IndexPublication? _pendingPublication;
    private IndexPublication? _appliedPublication;
    private bool _publicationQueued;

    private sealed record IndexBuildRequest(DirectorySession Session, DirectorySessionChange? Change,
        EntrySort Sort, EntryFilter Filter, IReadOnlySet<int>? MatchingTagIds,
        long BuildVersion, long SemanticVersion, CancellationTokenSource Cancellation);

    private sealed record IndexPublication(DirectorySession Session, EntryViewIndex Index,
        DirectorySessionChange Change, long BuildVersion, long SemanticVersion);
    private readonly DirectoryWatchBuffer _watchQueue = new(WatchQueueLimit);
    private long _watchScheduledGeneration;
    private bool _watchBatchActive;
    private long _entryRefreshGeneration;
    private bool _entryRefreshRequested;

    private DirectorySession? _session;
    private CancellationTokenSource? _listenCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _indexBuildCts;
    private CancellationTokenSource? _watchRefreshCts;
    private long _indexBuildVersion;
    private long _indexSemanticVersion;
    private long _appliedIndexBuildVersion;
    private Task _releasedFolder = Task.CompletedTask;
    private EntryViewIndex? _viewIndex;
    private EntryViewBinding? _viewBinding;
    private long _nextNavigationDiagnosticOperation;
    internal void TraceNextNavigation(long operation) => _nextNavigationDiagnosticOperation = operation;
    private EntrySort _sort = EntrySort.Name;
    private EntryFilter _filter = EntryFilter.None;
    private TagFilterState? _tagFilter;
    private string _addressText = string.Empty;
    private string _statusText = "Ready";
    private string? _errorText;
    private bool _isLoading;
    private int _itemCount;
    private bool _disposed;

    public PaneViewModel(
        IUiDispatcher dispatcher,
        IPathService paths,
        IDirectoryEnumerator enumerator,
        IComparer<string> names,
        IDirectoryWatcher? watcher = null,
        PaneId? paneId = null)
        : this(dispatcher, paths, enumerator, names, watcher, paneId, TimeProvider.System)
    {
    }

    internal PaneViewModel(
        IUiDispatcher dispatcher,
        IPathService paths,
        IDirectoryEnumerator enumerator,
        IComparer<string> names,
        IDirectoryWatcher? watcher,
        PaneId? paneId,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _dispatcher = dispatcher;
        _paths = paths;
        _enumerator = enumerator;
        _names = names;
        _watcher = watcher;
        _navigation = new NavigationController(paths, paneId ?? PaneId.New());
        PlaceholderNames = [];
        WhenCurrentSessionCompletes = Task.CompletedTask;
        ExplorerPreferenceBridge.FolderSizesChanged += OnFolderSizesChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<string>? OpenFileRequested;

    public EntrySort Sort => _sort;

    public string FilterQuery => _filter.Query;

    public string? TagFilterLabel => _tagFilter?.Label;

    public string AddressText
    {
        get => _addressText;
        private set => SetField(ref _addressText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set => SetError(value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public int ItemCount
    {
        get => _itemCount;
        private set => SetField(ref _itemCount, value);
    }

    public bool CanGoBack => _navigation.CanGoBack;

    public bool IsPortableDevice => FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(AddressText, out _);
    public bool CanReceiveFiles => !IsPortableDevice || _enumerator is PortableDeviceDirectoryEnumerator device
        && device.CanReceiveFiles(AddressText, _navigation.CurrentGeneration);

    public bool CanGoForward => _navigation.CanGoForward;

    public bool CanGoUp => _navigation.CanGoUp;

    public bool CanRefresh => !string.IsNullOrEmpty(AddressText);

    public static TimeSpan WatchDebounce { get; } = TimeSpan.FromMilliseconds(200);

    public const int WatchBatchSize = 32;

    public const int WatchQueueLimit = 4_096;

    private const int BackgroundIndexThreshold = 2_048;
    private static readonly TimeSpan EnumerationIndexInterval = TimeSpan.FromMilliseconds(100);

    public IReadOnlyList<string> PlaceholderNames { get; private set; }

    public EntryStore? Store => _recursiveProjection?.Store ?? _session?.Store;

    public EntryViewIndex? ViewIndex => _viewIndex;

    public EntryViewBinding? ViewBinding => _viewBinding;

    public Task WhenCurrentSessionCompletes { get; private set; }

    public Task WhenFolderReleased => _recursiveReleased.IsCompleted ? _releasedFolder : Task.WhenAll(_releasedFolder, _recursiveReleased);

    public NavigationController Navigation => _navigation;
    public void RestoreNavigationHistory(NavigationHistoryState state)
    {
        AssertUi();
        _navigation.RestoreHistory(state);
        RaiseToolbar();
    }

    public void Navigate(string path) => NavigateCore(path, allowFileActivation: true);

    /// <summary>Navigates without ever treating the destination as a file to execute.</summary>
    public void NavigateDirectory(string path) => NavigateCore(path, allowFileActivation: false);

    private void NavigateCore(string path, bool allowFileActivation)
    {
        AssertUi();
        var beforeRecursiveExit = _navigation.CurrentGeneration;
        EndRecursiveMode();
        if (_disposed || beforeRecursiveExit != _navigation.CurrentGeneration) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var previousGeneration = _navigation.CurrentGeneration;
        AddressText = path;
        if (_disposed || previousGeneration != _navigation.CurrentGeneration) return;
        ErrorText = null;
        if (_disposed || previousGeneration != _navigation.CurrentGeneration) return;
        RaiseToolbar();
        if (_disposed || previousGeneration != _navigation.CurrentGeneration) return;

        string normalized;
        try
        {
            normalized = _paths.Normalize(path);
        }
        catch (ArgumentException ex)
        {
            var generation = _navigation.ParkCurrentForRecovery();
            CancelIndexBuild();
            if (_disposed || !_navigation.Allows(generation)) return;
            WhenCurrentSessionCompletes = Task.CompletedTask;
            _viewIndex = null;
            _viewBinding = null;
            PlaceholderNames = [];
            _releasedFolder = DisposeCapturedAsync();
            if (!OwnsNavigation(generation)) return;
            ErrorText = ex.Message;
            if (!OwnsNavigation(generation)) return;
            IsLoading = false;
            if (!OwnsNavigation(generation)) return;
            StatusText = ex.Message;
            if (!OwnsNavigation(generation)) return;
            ItemCount = 0;
            if (!OwnsNavigation(generation)) return;
            OnPropertyChanged(nameof(PlaceholderNames));
            if (!OwnsNavigation(generation)) return;
            OnPropertyChanged(nameof(ViewIndex));
            if (!OwnsNavigation(generation)) return;
            OnPropertyChanged(nameof(ViewBinding));
            if (!OwnsNavigation(generation)) return;
            OnPropertyChanged(nameof(Store));
            if (!OwnsNavigation(generation)) return;
            RaiseToolbar();
            return;
        }

        if (TryOpenAsFile(normalized, allowFileActivation, out var parent))
        {
            normalized = parent;
        }
        if (_disposed || previousGeneration != _navigation.CurrentGeneration) return;

        AddressText = normalized;
        if (_disposed || previousGeneration != _navigation.CurrentGeneration) return;
        ResetFilters();
        if (_disposed || previousGeneration != _navigation.CurrentGeneration) return;
        var intent = _navigation.Open(normalized, recordHistory: true);
        StartSession(intent);
    }

    public void Back()
    {
        AssertUi();
        if (!CanGoBack) return;
        var beforeRecursiveExit = _navigation.CurrentGeneration;
        EndRecursiveMode();
        if (_disposed || beforeRecursiveExit != _navigation.CurrentGeneration) return;
        var intent = _navigation.Back();
        if (intent is null)
        {
            return;
        }

        if (!OwnsNavigation(intent.Value.Generation)) return;
        AddressText = intent.Value.Path;
        if (!OwnsNavigation(intent.Value.Generation)) return;
        ErrorText = null;
        if (!OwnsNavigation(intent.Value.Generation)) return;
        ResetFilters();
        if (!OwnsNavigation(intent.Value.Generation)) return;
        StartSession(intent.Value);
    }

    public void Forward()
    {
        AssertUi();
        if (!CanGoForward) return;
        var beforeRecursiveExit = _navigation.CurrentGeneration;
        EndRecursiveMode();
        if (_disposed || beforeRecursiveExit != _navigation.CurrentGeneration) return;
        var intent = _navigation.Forward();
        if (intent is null)
        {
            return;
        }

        if (!OwnsNavigation(intent.Value.Generation)) return;
        AddressText = intent.Value.Path;
        if (!OwnsNavigation(intent.Value.Generation)) return;
        ErrorText = null;
        if (!OwnsNavigation(intent.Value.Generation)) return;
        ResetFilters();
        if (!OwnsNavigation(intent.Value.Generation)) return;
        StartSession(intent.Value);
    }

    public void Up()
    {
        AssertUi();
        if (!CanGoUp) return;
        var beforeRecursiveExit = _navigation.CurrentGeneration;
        EndRecursiveMode();
        if (_disposed || beforeRecursiveExit != _navigation.CurrentGeneration) return;
        var intent = _navigation.Up();
        if (intent is null)
        {
            return;
        }

        if (!OwnsNavigation(intent.Value.Generation)) return;
        AddressText = intent.Value.Path;
        if (!OwnsNavigation(intent.Value.Generation)) return;
        ErrorText = null;
        if (!OwnsNavigation(intent.Value.Generation)) return;
        ResetFilters();
        if (!OwnsNavigation(intent.Value.Generation)) return;
        StartSession(intent.Value);
    }

    public void Refresh()
    {
        AssertUi();
        if (IsRecursive) { _ = RestartRecursiveAsync(); return; }
        if (!string.IsNullOrEmpty(AddressText) && !HomeLocation.IsHome(AddressText) && !TagLocation.IsTag(AddressText))
            ExplorerPreferenceBridge.InvalidateFolderSizes(AddressText);
        RefreshSession(preserveSurface: false);
    }

    public void RefreshAfterFileOperation()
    {
        AssertUi();
        if (IsRecursive) { MarkRecursiveStale(); return; }
        if (_disposed || string.IsNullOrEmpty(AddressText) || HomeLocation.IsHome(AddressText)) return;
        if (IsPortableDevice || TagLocation.IsTag(AddressText))
        {
            RefreshSession(preserveSurface: true);
            return;
        }
        _entryRefreshRequested = true;
        DirectorySession? session;
        CancellationToken token;
        lock (_sessionGate)
        {
            session = _session;
            token = _listenCts?.Token ?? CancellationToken.None;
        }
        if (session is null || IsLoading || _watchBatchActive || _entryRefreshGeneration == session.Generation) return;
        _entryRefreshRequested = false;
        _entryRefreshGeneration = session.Generation;
        _ = RefreshEntriesAsync(session, token);
    }

    public long EntryRevision { get; private set; }

    private async Task RefreshEntriesAsync(DirectorySession session, CancellationToken token)
    {
        var listing = new List<FileEntryCore>();
        Exception? failure = null;
        var complete = false;
        try
        {
            var request = new DirectoryRequest(session.PaneId, session.Generation, session.Path, session.Options);
            await foreach (var batch in _enumerator.EnumerateAsync(request, token).ConfigureAwait(false))
            {
                if (batch.Generation != session.Generation || batch.PaneId != session.PaneId
                    || !string.Equals(batch.DirectoryPath, session.Path, StringComparison.OrdinalIgnoreCase)) continue;
                if (batch.Error is { } error) throw new IOException(error.Message);
                listing.AddRange(batch.Entries);
                complete |= batch.IsFinal;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { failure = error; }
        _dispatcher.Post(() =>
        {
            if (_entryRefreshGeneration == session.Generation) _entryRefreshGeneration = 0;
            if (_disposed || token.IsCancellationRequested || !_navigation.Allows(session.Generation)) return;
            if (failure is not null) ReportUserError(failure.Message);
            else if (complete)
            {
                var changed = session.Store.Reconcile(listing);
                foreach (var name in changed)
                    ExplorerPreferenceBridge.MarkFolderSizeChanged(_paths.Combine(session.Path, name), true);
                if (changed.Count > 0) { EntryRevision++; RebuildIndex(); }
            }
            if (_entryRefreshRequested) RefreshAfterFileOperation();
            else if (!_watchQueue.IsEmpty) ApplyWatchBatch();
        });
    }

    private void ResetFilters()
    {
        _filter = EntryFilter.None;
        _tagFilter = null;
        OnPropertyChanged(nameof(FilterQuery));
        OnPropertyChanged(nameof(TagFilterLabel));
        OnPropertyChanged(nameof(MetadataFilter));
    }

    public void Open(in FileEntryCore entry)
    {
        AssertUi();
        var full = FullPath(entry);
        if (string.IsNullOrEmpty(full))
        {
            return;
        }

        if (entry.Kind == EntryKind.Directory)
        {
            Navigate(full);
            return;
        }

        OpenFileRequested?.Invoke(this, full);
    }

    public string FullPath(in FileEntryCore entry)
    {
        if (_recursiveProjection is { } recursive && recursive.IsVisible(entry.Id))
            return recursive.ResolvePath(entry.Id);
        if (IsPortableDevice && _enumerator is PortableDeviceDirectoryEnumerator device)
            return device.Resolve(AddressText, _navigation.CurrentGeneration, entry.Id) ?? string.Empty;
        if (Path.IsPathRooted(entry.Name))
        {
            return entry.Name;
        }

        var root = _navigation.CurrentPath ?? AddressText;
        return string.IsNullOrEmpty(root) ? entry.Name : _paths.Combine(root, entry.Name);
    }

    private bool TryOpenAsFile(string path, bool allowFileActivation, out string parent) =>
        NavigationFileActivation.TryOpen(path, allowFileActivation, File.Exists, Directory.Exists,
            _paths.GetParent, file => OpenFileRequested?.Invoke(this, file), out parent);

    public void SetSortColumn(EntrySortColumn column, bool defaultAscending = true)
    {
        AssertUi();
        if (IsRecursive && column == EntrySortColumn.ShellProperty) { ReportUserError(StringTable.Get("Recursive_SortUnavailable")); return; }
        _sort = _sort.SelectColumn(column, defaultAscending);
        OnPropertyChanged(nameof(Sort));
        RebuildIndex();
    }

    public void RestoreSort(EntrySort sort)
    {
        AssertUi();
        if (IsRecursive && sort.Column == EntrySortColumn.ShellProperty) { ReportUserError(StringTable.Get("Recursive_SortUnavailable")); return; }
        if (IsRecursive) sort = RecursiveSnapshot.NormalizeTreeSort(sort);
        if (_sort == sort) return;
        _sort = sort;
        OnPropertyChanged(nameof(Sort));
        RebuildIndex();
    }

    public void SetFilterQuery(string query)
    {
        AssertUi();
        if (_filter.Query == (query ?? string.Empty)) return;
        var previous = _filter.Query;
        _filter = _filter with { Query = query ?? string.Empty };
        // A cancellation callback may synchronously submit a newer query/navigation.
        var revision = CancelIndexBuild();
        if (_disposed || revision != Volatile.Read(ref _indexBuildVersion)) return;
        OnPropertyChanged(nameof(FilterQuery));
        if (IsRecursive) { QueueRecursiveProjection(semanticChange: true); return; }
        // Clearing is immediate and does not need to wait for a store count held by an old sort.
        if (!string.IsNullOrEmpty(_filter.Query)
            && EntryViewIndex.ShouldDebounce(Store?.Count ?? 0, previous, _filter.Query))
        {
            _ = RebuildIndexDebouncedAsync();
            return;
        }

        RebuildIndex();
    }

    public void SetTagFilter(string label, IEnumerable<int> matchingEntryIds)
    {
        AssertUi();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(matchingEntryIds);
        _tagFilter = new TagFilterState(label, matchingEntryIds.ToHashSet());
        OnPropertyChanged(nameof(TagFilterLabel));
        RebuildIndex();
    }

    public void ClearTagFilter()
    {
        AssertUi();
        if (_tagFilter is null)
        {
            return;
        }

        _tagFilter = null;
        OnPropertyChanged(nameof(TagFilterLabel));
        RebuildIndex();
    }

    public void ReportUserError(string message)
    {
        // An operation failure does not mean that the directory failed to load.
        StatusText = message;
    }

    public bool DisconnectVolume(uint removedLetters)
    {
        AssertUi();
        var path = AddressText;
        if (_disposed || path is not { Length: >= 3 } || path[1] != ':' || path[2] != '\\') return false;
        var letter = char.ToUpperInvariant(path[0]) - 'A';
        if (letter is < 0 or >= 26 || (removedLetters & (1u << letter)) == 0) return false;
        // Invalidate the generation before draining the old read/watch task. Late
        // notifications must not refill the view with entries from an unplugged disk.
        _ = _navigation.ParkCurrentForRecovery();
        if (!ShowHome()) return true;
        ItemCount = 0;
        ErrorText = StringTable.Get("Device_Unavailable");
        StatusText = ErrorText;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EndRecursiveMode(restoreOrdinary: false);
        DeferRecursiveProgress = null;
        if (_enumerator is PortableDeviceDirectoryEnumerator device) device.Clear();
        // Release the large folder index even while cancellation is still draining.
        _viewIndex = null;
        _viewBinding = null;
        _tagFilter = null;
        PlaceholderNames = [];
        ExplorerPreferenceBridge.FolderSizesChanged -= OnFolderSizesChanged;
        CancelWatchRefresh();
        ClearWatchQueue();
        CancelIndexBuild();

        await DisposeCapturedAsync().ConfigureAwait(false);
        await _recursiveReleased.ConfigureAwait(false);
    }

    private void RefreshSession(bool preserveSurface)
    {
        if (_disposed || string.IsNullOrEmpty(AddressText))
        {
            return;
        }

        var generation = _navigation.CurrentGeneration;
        try
        {
            var normalized = _paths.Normalize(AddressText);
            AddressText = normalized;
            if (!OwnsNavigation(generation)) return;
            ErrorText = null;
            if (!OwnsNavigation(generation)) return;
            var intent = _navigation.CurrentPath is not null
                ? _navigation.Refresh()
                : _navigation.Open(normalized, recordHistory: true);
            if (intent is not null)
            {
                generation = intent.Value.Generation;
                StartSession(intent.Value, preserveSurface);
            }
        }
        catch (ArgumentException ex)
        {
            if (!OwnsNavigation(generation)) return;
            ErrorText = ex.Message;
            if (!OwnsNavigation(generation)) return;
            StatusText = ex.Message;
            if (!OwnsNavigation(generation)) return;
            IsLoading = false;
        }
    }

    private void StartSession(NavigationIntent intent, bool preserveSurface = false)
    {
        if (!OwnsNavigation(intent.Generation)) return;
        PerformanceTrace.Record(PerformanceStage.NavigationReceived, intent.PaneId.Value, intent.Generation,
            operation: _nextNavigationDiagnosticOperation);
        _nextNavigationDiagnosticOperation = 0;
        EndRecursiveMode(restoreOrdinary: false);
        if (!OwnsNavigation(intent.Generation)) return;
        _entryRefreshRequested = false;
        if (HomeLocation.IsHome(intent.Path))
        {
            ShowHome();
            return;
        }

        if (!preserveSurface)
        {
            _viewIndex = null;
            _viewBinding = null;
            PlaceholderNames = [];
        }
        IsLoading = true;
        if (!OwnsNavigation(intent.Generation)) return;
        if (!preserveSurface)
        {
            ItemCount = 0;
            if (!OwnsNavigation(intent.Generation)) return;
            OnPropertyChanged(nameof(PlaceholderNames));
            if (!OwnsNavigation(intent.Generation)) return;
            OnPropertyChanged(nameof(ViewIndex));
            if (!OwnsNavigation(intent.Generation)) return;
            OnPropertyChanged(nameof(ViewBinding));
            if (!OwnsNavigation(intent.Generation)) return;
            OnPropertyChanged(nameof(Store));
            if (!OwnsNavigation(intent.Generation)) return;
            StatusText = "Loading…";
            if (!OwnsNavigation(intent.Generation)) return;
        }

        RaiseToolbar();
        if (!OwnsNavigation(intent.Generation)) return;
        CancelWatchRefresh();
        if (!OwnsNavigation(intent.Generation)) return;
        ClearWatchQueue();
        CancelIndexBuild();
        if (_disposed || !_navigation.Allows(intent.Generation)) return;

        DirectorySession? previous;
        CancellationTokenSource? previousListen;
        lock (_sessionGate)
        {
            previous = _session;
            previousListen = _listenCts;
            _session = null;
            _listenCts = null;
        }

        _releasedFolder = DisposeCapturedAsync(previous, previousListen);
        if (!OwnsNavigation(intent.Generation)) return;

        var request = new DirectoryRequest(
            intent.PaneId,
            intent.Generation,
            intent.Path,
            DirectoryReadOptions.Default with
            {
                IncludeHidden = ExplorerPreferenceBridge.ShowHiddenFiles(),
                IncludeSystem = ExplorerPreferenceBridge.ShowHiddenFiles(),
            });
        var session = DirectorySession.Start(request, _enumerator);
        var listen = new CancellationTokenSource();
        if (!OwnsNavigation(intent.Generation))
        {
            _ = DisposeCapturedAsync(session, listen);
            return;
        }
        lock (_sessionGate)
        {
            _session = session;
            _listenCts = listen;
        }

        WhenCurrentSessionCompletes = session.WhenCompleted;
        _ = PumpAsync(session, listen.Token);
        _ = ListenForChangesAsync(request, listen.Token);
    }

    private bool ShowHome()
    {
        var generation = _navigation.CurrentGeneration;
        EndRecursiveMode(restoreOrdinary: false);
        if (!OwnsNavigation(generation)) return false;
        CancelWatchRefresh();
        if (!OwnsNavigation(generation)) return false;
        ClearWatchQueue();
        CancelIndexBuild();
        if (_disposed || !_navigation.Allows(generation)) return false;
        _viewIndex = null;
        _viewBinding = null;
        PlaceholderNames = [];
        WhenCurrentSessionCompletes = Task.CompletedTask;
        _releasedFolder = DisposeCapturedAsync();
        if (!OwnsNavigation(generation)) return false;
        IsLoading = false;
        if (!OwnsNavigation(generation)) return false;
        ErrorText = null;
        if (!OwnsNavigation(generation)) return false;
        ItemCount = 1; // Home is a dashboard; never probe disks to update its status.
        if (!OwnsNavigation(generation)) return false;
        StatusText = StringTable.Get("Home");
        if (!OwnsNavigation(generation)) return false;
        OnPropertyChanged(nameof(PlaceholderNames));
        if (!OwnsNavigation(generation)) return false;
        OnPropertyChanged(nameof(ViewIndex));
        if (!OwnsNavigation(generation)) return false;
        OnPropertyChanged(nameof(ViewBinding));
        if (!OwnsNavigation(generation)) return false;
        OnPropertyChanged(nameof(Store));
        if (!OwnsNavigation(generation)) return false;
        RaiseToolbar();
        return OwnsNavigation(generation);
    }

    private bool OwnsNavigation(long generation) => !_disposed && _navigation.Allows(generation);

    private Task DisposeCapturedAsync()
    {
        DirectorySession? session;
        CancellationTokenSource? listen;
        lock (_sessionGate)
        {
            session = _session;
            listen = _listenCts;
            _session = null;
            _listenCts = null;
        }

        return DisposeCapturedAsync(session, listen);
    }

    private async Task ListenForChangesAsync(DirectoryRequest request, CancellationToken cancellationToken)
    {
        if (_watcher is null || !CanWatch(request.Path))
        {
            return;
        }

        try
        {
            await foreach (var notice in _watcher.WatchAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (!_navigation.Allows(request.Generation)
                    || notice.Kind == DirectoryWatchKind.WatcherDisabled)
                {
                    return;
                }

                _watchQueue.Enqueue(notice);
                ScheduleWatchApply(request.Generation);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // ponytail: a dead watcher stays quiet until the next navigation; no crash toast.
        }
    }

    private void ScheduleWatchApply(long generation)
    {
        if (!_navigation.Allows(generation))
            return;
        if (Interlocked.CompareExchange(ref _watchScheduledGeneration, generation, 0) != 0)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed || !_navigation.Allows(generation))
            {
                Interlocked.CompareExchange(ref _watchScheduledGeneration, 0, generation);
                if (!_disposed && !_watchQueue.IsEmpty)
                    ScheduleWatchApply(_navigation.CurrentGeneration);
                return;
            }

            _ = DebouncedWatchApplyAsync(generation);
        });
    }

    private async Task DebouncedWatchApplyAsync(long generation)
    {
        CancelWatchRefresh();
        var cts = new CancellationTokenSource();
        _watchRefreshCts = cts;
        try
        {
            await Task.Delay(WatchDebounce, cts.Token).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                Interlocked.CompareExchange(ref _watchScheduledGeneration, 0, generation);
                if (_disposed || !_navigation.Allows(generation))
                {
                    return;
                }

                ApplyWatchBatch();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyWatchBatch()
    {
        if (_disposed || IsLoading)
        {
            return;
        }

        DirectorySession? session;
        lock (_sessionGate)
        {
            session = _session;
        }

        if (session is null || !_navigation.Allows(session.Generation))
        {
            return;
        }

        if (_watchBatchActive || _entryRefreshGeneration == session.Generation) return;
        var notices = new List<DirectoryWatchNotification>();
        while (notices.Count < WatchBatchSize && _watchQueue.TryDequeue(out var notice))
        {
            if (notice.Generation != session.Generation) continue;
            if (notice.Kind == DirectoryWatchKind.Overflow)
            {
                ClearWatchQueue();
                RefreshSession(preserveSurface: true);
                return;
            }
            notices.Add(notice);
        }
        if (notices.Count == 0) return;
        _watchBatchActive = true;
        _ = PrepareWatchBatchAsync(session, notices);
    }

    private async Task PrepareWatchBatchAsync(DirectorySession session, List<DirectoryWatchNotification> notices)
    {
        var prepared = new List<(DirectoryWatchNotification Notice, FileEntryCore? Entry)>();
        var failed = false;
        try
        {
            await Task.Run(() =>
            {
                foreach (var notice in notices)
                {
                    if (!_navigation.Allows(session.Generation)) break;
                    FileEntryCore? entry = null;
                    if (notice.Kind is DirectoryWatchKind.Created or DirectoryWatchKind.Modified or DirectoryWatchKind.Renamed
                        && LiveDirectoryEntry.TryRead(session.Path, notice.Name, session.Options, out var read)) entry = read;
                    prepared.Add((notice, entry));
                }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failed = true;
            Trace.TraceError("Directory watch metadata read failed: {0}", error);
        }
        finally
        {
            _dispatcher.Post(() =>
            {
                _watchBatchActive = false;
                if (_disposed) return;
                if (_navigation.Allows(session.Generation))
                {
                    if (failed)
                    {
                        ClearWatchQueue();
                        RefreshSession(preserveSurface: true);
                        return;
                    }
                    var mutated = false;
                    foreach (var (notice, entry) in prepared)
                    {
                        if (!string.IsNullOrEmpty(notice.Name))
                            ExplorerPreferenceBridge.MarkFolderSizeChanged(_paths.Combine(session.Path, notice.Name), notice.Kind != DirectoryWatchKind.Modified);
                        if (!string.IsNullOrEmpty(notice.OldName))
                            ExplorerPreferenceBridge.MarkFolderSizeChanged(_paths.Combine(session.Path, notice.OldName), true);
                        mutated |= ApplyWatchNotice(session, notice, entry);
                    }
                    if (mutated) { EntryRevision++; RebuildIndex(); }
                }
                if (_entryRefreshRequested) RefreshAfterFileOperation();
                else if (!_watchQueue.IsEmpty) ApplyWatchBatch();
            });
        }
    }

    private static bool ApplyWatchNotice(DirectorySession session, DirectoryWatchNotification notice, FileEntryCore? entry)
    {
        if (notice.Kind == DirectoryWatchKind.Deleted)
            return !string.IsNullOrEmpty(notice.Name) && session.Store.RemoveByName(notice.Name);
        if (notice.Kind == DirectoryWatchKind.Renamed)
        {
            if (entry is { } renamed)
            {
                session.Store.Rename(string.IsNullOrEmpty(notice.OldName) ? notice.Name : notice.OldName, renamed);
                return true;
            }
            return !string.IsNullOrEmpty(notice.OldName) && session.Store.RemoveByName(notice.OldName);
        }
        if (entry is { } changed)
        {
            session.Store.Upsert(changed);
            return true;
        }
        return false;
    }

    private void CancelWatchRefresh()
    {
        var previous = Interlocked.Exchange(ref _watchRefreshCts, null);
        if (previous is null)
        {
            return;
        }

        previous.Cancel();
        previous.Dispose();
    }

    private void ClearWatchQueue()
    {
        _watchQueue.Clear();
        Interlocked.Exchange(ref _watchScheduledGeneration, 0);
    }

    private static bool CanWatch(string path)
    {
        if (string.IsNullOrEmpty(path) || HomeLocation.IsHome(path) || TagLocation.IsTag(path)
            || PortableDeviceLocation.TryParse(path, out _))
        {
            return false;
        }

        try
        {
            return Path.IsPathRooted(path) && Directory.Exists(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task DisposeCapturedAsync(DirectorySession? session, CancellationTokenSource? listen)
    {
        if (listen is not null)
        {
            await listen.CancelAsync().ConfigureAwait(false);
            listen.Dispose();
        }

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(DirectorySession session, CancellationToken cancellationToken)
    {
        try
        {
            DirectorySessionChange? lastChange = null;
            long? lastRequestAt = null;
            await foreach (var change in session.ReadChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_navigation.Allows(session.Generation))
                {
                    return;
                }

                // First content is immediate. Later appends use a non-resetting cadence:
                // the bounded session channel coalesces notifications while we wait.
                // Completion wakes the delay, so the exact terminal build is not paced.
                if (lastRequestAt is { } previous && IsProgress(change)
                    && !session.WhenCompleted.IsCompleted)
                {
                    var remaining = EnumerationIndexInterval - _timeProvider.GetElapsedTime(previous);
                    if (remaining > TimeSpan.Zero)
                    {
                        using var pacing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        var delay = Task.Delay(remaining, _timeProvider, pacing.Token);
                        await Task.WhenAny(delay, session.WhenCompleted).ConfigureAwait(false);
                        pacing.Cancel();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                var latestChange = session.WhenCompleted.IsCompleted
                    ? new DirectorySessionChange(session.Store.Count, session.State, session.Error)
                    : change;
                if (lastChange != latestChange)
                {
                    BuildAndPublishIndex(session, latestChange, cancellationToken);
                    lastRequestAt = _timeProvider.GetTimestamp();
                    lastChange = latestChange;
                }
            }

            if (_navigation.Allows(session.Generation))
            {
                var finalChange = new DirectorySessionChange(session.Store.Count, session.State, session.Error);
                // The coalesced channel normally ends with this exact final notification.
                // Keep the fallback for exceptions/cancellation that end without publishing it.
                if (lastChange != finalChange)
                    BuildAndPublishIndex(session, finalChange);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsProgress(DirectorySessionChange change) =>
        change.State is DirectorySessionState.Loading or DirectorySessionState.Partial;

    public void RebuildViewIndex() => RebuildIndex();

    private ulong SizeOf(FileEntryCore entry, DirectorySession session)
    {
        if (entry.Kind != EntryKind.Directory || !ExplorerPreferenceBridge.ShowFolderSizes())
        {
            return entry.Size;
        }

        var path = PortableDeviceLocation.TryParse(session.Path, out _) && _enumerator is PortableDeviceDirectoryEnumerator device
            ? device.Resolve(session.Path, session.Generation, entry.Id) ?? string.Empty
            : Path.IsPathRooted(entry.Name) ? entry.Name : _paths.Combine(session.Path, entry.Name);
        return ExplorerPreferenceBridge.TryFolderSize(path, out var size)
            || ExplorerPreferenceBridge.TryLastKnownFolderSize(path, out size) ? size : 0;
    }

    private void OnFolderSizesChanged(string path)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed || _sort.Column != EntrySortColumn.Size || !ExplorerPreferenceBridge.ShowFolderSizes()
                || !string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(AddressText), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = RebuildIndexDebouncedAsync();
        });
    }

    private void RebuildIndex()
    {
        if (IsRecursive) { QueueRecursiveProjection(semanticChange: true); return; }
        DirectorySession? session;
        lock (_sessionGate)
        {
            session = _session;
        }

        if (session is null)
        {
            return;
        }

        BuildAndPublishIndex(session, change: null);
    }

    private void BuildAndPublishIndex(
        DirectorySession session,
        DirectorySessionChange? change,
        CancellationToken sessionCancellation = default)
    {
        IndexBuildRequest? start;
        lock (_indexBuildGate)
            start = RequestIndexBuildCore(session, change, sessionCancellation);
        if (start is not null) StartIndexBuild(start);
    }

    // Only request bookkeeping runs under this gate. Never read the store or invoke a
    // comparer/dispatcher here: newer UI requests must be able to revoke obsolete work.
    private IndexBuildRequest? RequestIndexBuildCore(DirectorySession session,
        DirectorySessionChange? change, CancellationToken sessionCancellation = default)
    {
        if (_disposed || !_navigation.Allows(session.Generation)) return null;
        long revision;
        if (change is null)
        {
            revision = CancelIndexBuildCore();
        }
        else
        {
            // Enumeration only extends the same query. Keep the active sort and any
            // queued content eligible; only the newest never-started request is needed.
            // A semantic debounce owns its query until expiry builds the current data
            // and current session state, including a terminal notification seen here.
            if (_filterCts is not null) return null;
            revision = _indexBuildVersion;
            var pending = _pendingIndexRequest;
            _pendingIndexRequest = null;
            if (ReferenceEquals(_indexBuildCts, pending?.Cancellation)) _indexBuildCts = null;
            CancelIndexToken(pending?.Cancellation);
            pending?.Cancellation.Dispose();
        }
        if (_disposed || revision != _indexBuildVersion || !_navigation.Allows(session.Generation)) return null;
        var buildCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _indexBuildCts = buildCts;
        var request = new IndexBuildRequest(session, change, _sort, _filter, _tagFilter?.MatchingEntryIds,
            Interlocked.Increment(ref _indexBuildVersion), _indexSemanticVersion, buildCts);
        if (PerformanceTrace.Enabled) PerformanceTrace.Record(PerformanceStage.BuildRequested,
            session.PaneId.Value, session.Generation, request.BuildVersion, change?.EntryCount ?? 0);
        if (_activeIndexRequest is not null)
        {
            _pendingIndexRequest = request;
            return null;
        }
        _activeIndexRequest = request;
        return request;
    }

    private void StartIndexBuild(IndexBuildRequest request)
    {
        EntrySnapshot? snapshot = null;
        var background = false;
        try
        {
            var token = request.Cancellation.Token;
            token.ThrowIfCancellationRequested();
            // An unfinished enumeration can grow between Count and CaptureSnapshot.
            // Keep that entire path off the requester, while idle, completed small views
            // still update immediately without a task hop or an intermediate empty view.
            background = !(request.Session.WhenCompleted.IsCompleted
                    || request.Session.State == DirectorySessionState.Completed)
                || request.Sort.Column == EntrySortColumn.ShellProperty
                || request.Session.Store.Count >= BackgroundIndexThreshold;
            if (!background)
            {
                snapshot = request.Session.Store.CaptureSnapshot(token, request.Session.PaneId.Value,
                    request.Session.Generation, request.BuildVersion);
                background = snapshot.Count >= BackgroundIndexThreshold;
                if (!background) BuildAndPublishSnapshot(request, snapshot);
            }
        }
        catch (Exception error) { RecordIndexBuildFailure(request, error); }
        finally
        {
            if (!background)
            {
                var next = CompleteIndexBuild(request);
                if (next is not null) StartIndexWorker(next);
            }
        }
        if (background) StartIndexWorker(request, snapshot);
    }

    private void StartIndexWorker(IndexBuildRequest request, EntrySnapshot? snapshot = null)
    {
        // One task drains the latest-request slot. A burst of notifications does not
        // create one Task.Run/semaphore waiter (or one copied snapshot) per notification.
        _ = Task.Run(async () =>
        {
            IndexBuildRequest? current = request;
            while (current is not null)
            {
                try
                {
                    var token = current.Cancellation.Token;
                    token.ThrowIfCancellationRequested();
                    var entries = snapshot ?? current.Session.Store.CaptureSnapshot(token,
                        current.Session.PaneId.Value, current.Session.Generation, current.BuildVersion);
                    IReadOnlyDictionary<int, EntryPropertyValue>? properties = null;
                    if (current.Sort.Column == EntrySortColumn.ShellProperty && Models.DetailsColumn.IsPropertyName(current.Sort.PropertyName))
                    {
                        var values = new System.Collections.Concurrent.ConcurrentDictionary<int, EntryPropertyValue>();
                        await Parallel.ForEachAsync(entries, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, async (entry, propertyToken) =>
                        {
                            var path = Path.IsPathRooted(entry.Name) ? entry.Name : Path.Combine(current.Session.Path, entry.Name);
                            var result = await FilePropertyCache.GetAsync(path, entry, [current.Sort.PropertyName!], propertyToken).ConfigureAwait(false);
                            if (result.TryGetValue(current.Sort.PropertyName!, out var value)) values[entry.Id] = value;
                        }).ConfigureAwait(false);
                        properties = values;
                    }
                    BuildAndPublishSnapshot(current, entries, properties);
                }
                catch (Exception error) { RecordIndexBuildFailure(current, error); }
                finally
                {
                    snapshot = null;
                    current = CompleteIndexBuild(current);
                }
            }
        });
    }

    private void BuildAndPublishSnapshot(IndexBuildRequest request, EntrySnapshot snapshot,
        IReadOnlyDictionary<int, EntryPropertyValue>? properties = null)
    {
        var change = request.Change ?? new DirectorySessionChange(snapshot.Count, request.Session.State, request.Session.Error);
        var index = ReuseTerminalIndex(request, snapshot, change)
            ?? BuildIndex(request.Session, request.Sort, request.Filter, request.MatchingTagIds,
            request.Cancellation.Token, properties, request.BuildVersion, snapshot, IsProgress(change));
        Publish(request.Session, index, change, request.BuildVersion, request.SemanticVersion);
    }

    private EntryViewIndex? ReuseTerminalIndex(IndexBuildRequest request, EntrySnapshot snapshot,
        DirectorySessionChange change)
    {
        if (IsProgress(change)) return null;
        IndexPublication? previous;
        lock (_publicationGate) previous = _pendingPublication ?? Volatile.Read(ref _appliedPublication);
        // Completion may change only status. Reuse the sorted immutable rows only
        // for this exact snapshot and semantic request, then restore strict eligibility.
        return previous is not null && previous.SemanticVersion == request.SemanticVersion
            && ReferenceEquals(previous.Index.Snapshot, snapshot)
            ? previous.Index.AsFinal() : null;
    }

    private static void RecordIndexBuildFailure(IndexBuildRequest request, Exception error)
    {
        var cancelled = error is OperationCanceledException;
        if (!cancelled) Trace.TraceError("Directory index build failed: {0}", error);
        PerformanceTrace.Record(cancelled ? PerformanceStage.BuildCancelled : PerformanceStage.BuildAborted,
            request.Session.PaneId.Value, request.Session.Generation, request.BuildVersion,
            outcome: cancelled ? PerformanceOutcome.Cancelled : PerformanceOutcome.Failed);
    }

    private EntryViewIndex BuildIndex(
        DirectorySession session,
        EntrySort sort,
        EntryFilter filter,
        IReadOnlySet<int>? matchingTagIds,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues = null, long diagnosticBuild = 0,
        EntrySnapshot? snapshot = null, bool allowAppendProgress = false)
    {
        var started = PerformanceTrace.Enabled ? Stopwatch.GetTimestamp() : 0;
        var operation = diagnosticBuild;
        PerformanceTrace.Record(PerformanceStage.BuildStart, session.PaneId.Value, session.Generation, operation);
        var outcome = PerformanceOutcome.Completed;
        try
        {
        Func<FileEntryCore, bool>? tagMatch = matchingTagIds is null
            ? null
            : entry => matchingTagIds.Contains(entry.Id);
        snapshot ??= session.Store.CaptureSnapshot(cancellationToken, session.PaneId.Value, session.Generation, operation);
        return EntryViewIndex.Build(
            snapshot,
            sort,
            filter,
            _names,
            session.Generation,
            tagMatch,
            cancellationToken,
            entry => SizeOf(entry, session),
            propertyValues,
            allowAppendProgress);
        }
        catch (OperationCanceledException) { outcome = PerformanceOutcome.Cancelled; throw; }
        catch { outcome = PerformanceOutcome.Failed; throw; }
        finally
        {
            if (PerformanceTrace.Enabled) PerformanceTrace.Record(PerformanceStage.BuildEnd,
                session.PaneId.Value, session.Generation, operation, outcome: outcome,
                durationTicks: Stopwatch.GetTimestamp() - started);
        }
    }

    private long CancelIndexBuild()
    {
        lock (_indexBuildGate) return CancelIndexBuildCore();
    }

    private long CancelIndexBuildCore()
    {
        // Detach every old owner and revoke publication before invoking any callback.
        // Reentrant requests receive a later revision and must not be overwritten by
        // this older stack frame after cancellation returns.
        var previousFilter = _filterCts;
        _filterCts = null;
        var previous = _indexBuildCts;
        _indexBuildCts = null;
        var active = _activeIndexRequest?.Cancellation;
        var pendingRequest = _pendingIndexRequest;
        _pendingIndexRequest = null;
        var revision = Interlocked.Increment(ref _indexBuildVersion);
        Interlocked.Increment(ref _indexSemanticVersion);
        IndexPublication? pending;
        lock (_publicationGate)
        {
            pending = _pendingPublication;
            _pendingPublication = null;
            Volatile.Write(ref _appliedPublication, null);
        }
        if (pending is not null) PerformanceTrace.Record(PerformanceStage.PublishDropped,
            pending.Session.PaneId.Value, pending.Session.Generation, pending.BuildVersion,
            outcome: PerformanceOutcome.Dropped);
        CancelIndexToken(previousFilter);
        // Append requests no longer pre-cancel the active owner. A semantic change
        // must therefore revoke both it and the latest pending request explicitly.
        CancelIndexToken(active);
        if (!ReferenceEquals(previous, active)) CancelIndexToken(previous);
        // A waiting request has never borrowed its token or snapshot. The active owner
        // alone disposes its CTS after all work (including publication) has unwound.
        pendingRequest?.Cancellation.Dispose();
        return revision;
    }

    private IndexBuildRequest? CompleteIndexBuild(IndexBuildRequest request)
    {
        lock (_indexBuildGate)
        {
            if (ReferenceEquals(_indexBuildCts, request.Cancellation)) _indexBuildCts = null;
            request.Cancellation.Dispose();
            var next = _pendingIndexRequest;
            _pendingIndexRequest = null;
            _activeIndexRequest = next;
            return next;
        }
    }

    private static void CancelIndexToken(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (AggregateException error)
        {
            // Cancellation is already signalled even when a callback throws. Do not
            // abandon the latest request or strand its active/pending ownership.
            Trace.TraceError("Directory index cancellation callback failed: {0}", error);
        }
    }

    // Called only under _indexBuildGate. The waiting task owns disposal after it drains.
    private void CancelPendingIndexDebounceCore()
    {
        var pending = _filterCts;
        _filterCts = null;
        CancelIndexToken(pending);
    }

    private async Task RebuildIndexDebouncedAsync()
    {
        CancellationTokenSource cts;
        CancellationToken token;
        lock (_indexBuildGate)
        {
            if (_disposed) return;
            var revision = _indexBuildVersion;
            CancelPendingIndexDebounceCore();
            if (_disposed || revision != _indexBuildVersion || _filterCts is not null) return;
            _filterCts = cts = new CancellationTokenSource();
            token = cts.Token;
        }
        try
        {
            await Task.Delay(EntryViewIndex.DebounceDelay, _timeProvider, token).ConfigureAwait(false);
            DirectorySession? session;
            lock (_sessionGate) session = _session;
            IndexBuildRequest? start;
            lock (_indexBuildGate)
            {
                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_filterCts, cts)) return;
                // Claim and register atomically, but execute outside the request gate.
                _filterCts = null;
                start = session is null ? null : RequestIndexBuildCore(session, change: null);
            }
            if (start is not null) StartIndexBuild(start);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_indexBuildGate)
            {
                if (ReferenceEquals(_filterCts, cts)) _filterCts = null;
                cts.Dispose();
            }
        }
    }

    private void Publish(
        DirectorySession session,
        EntryViewIndex index,
        DirectorySessionChange change,
        long buildVersion,
        long semanticVersion)
    {
        IndexPublication? replaced;
        bool queue;
        lock (_publicationGate)
        {
            if (_disposed || semanticVersion != Volatile.Read(ref _indexSemanticVersion)
                || buildVersion <= Volatile.Read(ref _appliedIndexBuildVersion))
            {
                PerformanceTrace.Record(PerformanceStage.PublishDropped, session.PaneId.Value,
                    session.Generation, buildVersion, outcome: PerformanceOutcome.Dropped);
                return;
            }
            replaced = _pendingPublication;
            _pendingPublication = new(session, index, change, buildVersion, semanticVersion);
            queue = !_publicationQueued;
            _publicationQueued = true;
        }
        if (replaced is not null) PerformanceTrace.Record(PerformanceStage.PublishDropped,
            replaced.Session.PaneId.Value, replaced.Session.Generation, replaced.BuildVersion,
            outcome: PerformanceOutcome.Dropped);
        PerformanceTrace.Record(PerformanceStage.PublishQueued, session.PaneId.Value,
            session.Generation, buildVersion);
        if (queue)
        {
            try { _dispatcher.Post(DrainPublication); }
            catch
            {
                lock (_publicationGate) { _pendingPublication = null; _publicationQueued = false; }
                throw;
            }
        }
    }

    private void DrainPublication()
    {
        IndexPublication? publication;
        lock (_publicationGate)
        {
            publication = _pendingPublication;
            _pendingPublication = null;
            _publicationQueued = false;
        }
        // The queued callback retains only the pane. Replaced snapshots can be collected
        // even while the dispatcher is busy, and UI callbacks never run under this gate.
        if (publication is not null) ApplyPublication(publication);
    }

    private void ApplyPublication(IndexPublication publication)
    {
        var (session, index, change, buildVersion, semanticVersion) = publication;
        if (_disposed
            || !_navigation.Allows(session.Generation)
            || !ReferenceEquals(_session, session)
            || semanticVersion != Volatile.Read(ref _indexSemanticVersion)
            || buildVersion <= Volatile.Read(ref _appliedIndexBuildVersion)
            || _viewIndex is { } current && current.Snapshot.BelongsTo(session.Store)
                && index.DataVersion < current.DataVersion)
        {
            PerformanceTrace.Record(PerformanceStage.PublishDropped, session.PaneId.Value,
                session.Generation, buildVersion, outcome: PerformanceOutcome.Dropped);
            return;
        }

        if (!index.CanPublish(session.Store))
        {
            PerformanceTrace.Record(PerformanceStage.PublishDropped, session.PaneId.Value,
                session.Generation, buildVersion, outcome: PerformanceOutcome.Dropped);
            // If no newer semantic request took over, rebuild the latest store version.
            // The existing published snapshot remains coherent while that build runs.
            BuildAndPublishIndex(session, change: null);
            return;
        }

        // Publish the complete immutable tuple before even ItemCount can notify a
        // recycled presenter. Capture only path helpers/identity data, not a session.
        var binding = CreateViewBinding(session, index, buildVersion);
        var names = CaptureNames(session.Store, index);
        Volatile.Write(ref _appliedIndexBuildVersion, buildVersion);
        _viewIndex = index;
        _viewBinding = binding;
        Volatile.Write(ref _appliedPublication, publication);
        PlaceholderNames = names;
        ItemCount = index.Count;
        if (!OwnsPublication(publication, binding)) return;
        OnPropertyChanged(nameof(PlaceholderNames));
        if (!OwnsPublication(publication, binding)) return;
        OnPropertyChanged(nameof(ViewIndex));
        if (!OwnsPublication(publication, binding)) return;
        OnPropertyChanged(nameof(ViewBinding));
        if (!OwnsPublication(publication, binding)) return;
        OnPropertyChanged(nameof(Store));
        if (!OwnsPublication(publication, binding)) return;

        if (change.Error is not null)
        {
            ErrorText = change.Error.Message;
            if (!OwnsPublication(publication, binding)) return;
        }

        var loading = change.State is DirectorySessionState.Loading or DirectorySessionState.Partial;
        IsLoading = loading && change.Error is null;
        if (!OwnsPublication(publication, binding)) return;
        StatusText = FormatStatus(change, index.Count);
        if (!OwnsPublication(publication, binding)) return;
        RaiseToolbar();
        if (!OwnsPublication(publication, binding)) return;
        if (PerformanceTrace.Enabled) PerformanceTrace.Record(
            IsLoading ? PerformanceStage.ContentApplied : PerformanceStage.FinalApplied,
            session.PaneId.Value, session.Generation, buildVersion, index.Count,
            change.State switch
            {
                DirectorySessionState.Completed => PerformanceOutcome.Completed,
                DirectorySessionState.Cancelled => PerformanceOutcome.Cancelled,
                DirectorySessionState.Failed => PerformanceOutcome.Failed,
                _ => PerformanceOutcome.Partial
            });
        if (!IsLoading && _entryRefreshRequested)
        {
            RefreshAfterFileOperation();
        }
        else if (!IsLoading && !_watchQueue.IsEmpty)
        {
            ApplyWatchBatch();
        }
    }

    private bool OwnsPublication(IndexPublication publication, EntryViewBinding binding) =>
        !_disposed && _navigation.Allows(binding.Generation)
        && ReferenceEquals(_session, publication.Session)
        && ReferenceEquals(_viewBinding, binding)
        && ReferenceEquals(Volatile.Read(ref _appliedPublication), publication)
        && publication.SemanticVersion == Volatile.Read(ref _indexSemanticVersion);

    private EntryViewBinding CreateViewBinding(DirectorySession session, EntryViewIndex index, long operation)
    {
        var folderPath = session.Path;
        var paths = _paths;
        Func<FileEntryCore, string> resolvePath;
        if (PortableDeviceLocation.TryParse(folderPath, out _)
            && _enumerator is PortableDeviceDirectoryEnumerator device)
        {
            var locations = device.CaptureLocations(folderPath, session.Generation);
            resolvePath = entry => locations is not null && locations.TryGetValue(entry.Id, out var location)
                ? location : throw new InvalidOperationException("Entry has no identity in its published device folder.");
        }
        else
        {
            resolvePath = entry => Path.IsPathRooted(entry.Name)
                ? entry.Name : paths.Combine(folderPath, entry.Name);
        }
        return new EntryViewBinding(session.Store, index, session.Generation, folderPath, resolvePath, diagnosticOperation: operation);
    }

    private static IReadOnlyList<string> CaptureNames(EntryStore store, EntryViewIndex index)
    {
        var take = Math.Min(PlaceholderCap, index.Count);
        if (take == 0)
        {
            return [];
        }

        var names = new string[take];
        for (var i = 0; i < take; i++)
        {
            names[i] = index.GetEntry(store, i).Name;
        }

        return names;
    }

    private static string FormatStatus(DirectorySessionChange change, int count)
    {
        if (change.Error is not null)
        {
            return StatusForError(change.Error);
        }

        return change.State switch
        {
            DirectorySessionState.Loading or DirectorySessionState.Partial => StringTable.Format("Status_ItemsLoading", count),
            DirectorySessionState.Completed => StringTable.Format("Status_Items", count),
            DirectorySessionState.Cancelled => StringTable.Format("Status_ItemsCancelled", count),
            DirectorySessionState.Failed => StringTable.Format("Status_ItemsFailed", count),
            _ => StringTable.Format("Status_Items", count),
        };
    }

    private static string StatusForError(DirectoryReadError error) => error.Kind switch
    {
        DirectoryReadErrorKind.AccessDenied => StringTable.Get("AccessDenied_Title"),
        DirectoryReadErrorKind.NotFound => StringTable.Get("NotFound_Title"),
        DirectoryReadErrorKind.Offline => StringTable.Get("Offline_Title"),
        _ => StringTable.Get("OpenFailed_Title"),
    };

    private void RaiseToolbar()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(CanRefresh));
    }

    private bool TagMatch(FileEntryCore entry) => _tagFilter?.Matches(entry) ?? true;

    [Conditional("DEBUG")]
    private void AssertUi()
    {
        Debug.Assert(_dispatcher.HasThreadAccess, "PaneViewModel commands must run on the UI thread.");
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetField(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void SetError(string? value)
    {
        if (_errorText == value)
        {
            return;
        }

        _errorText = value;
        OnPropertyChanged(nameof(ErrorText));
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void SetField(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }
}
