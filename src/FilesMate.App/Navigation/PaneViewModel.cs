using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

using FilesMate.App.Localization;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.App.Navigation;

/// <summary>
/// Session state for one pane. Does not wrap each file in a view-model.
/// Thread-safety: public members except construction run on the UI dispatcher in the app.
/// Cancellation: a newer navigation disposes the previous <see cref="DirectorySession"/>.
/// </summary>
public sealed class PaneViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    public const int PlaceholderCap = 128;

    private readonly IUiDispatcher _dispatcher;
    private readonly IPathService _paths;
    private readonly IDirectoryEnumerator _enumerator;
    private readonly IDirectoryWatcher? _watcher;
    private readonly IComparer<string> _names;
    private readonly NavigationController _navigation;
    private readonly object _sessionGate = new();
    private readonly object _indexBuildGate = new();
    private readonly DirectoryWatchBuffer _watchQueue = new(WatchQueueLimit);
    private long _watchScheduledGeneration;

    private DirectorySession? _session;
    private CancellationTokenSource? _listenCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _indexBuildCts;
    private CancellationTokenSource? _watchRefreshCts;
    private long _indexBuildVersion;
    private Task _releasedFolder = Task.CompletedTask;
    private EntryViewIndex? _viewIndex;
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
    {
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

    public bool CanGoForward => _navigation.CanGoForward;

    public bool CanGoUp => _navigation.CanGoUp;

    public bool CanRefresh => !string.IsNullOrEmpty(AddressText);

    public static TimeSpan WatchDebounce { get; } = TimeSpan.FromMilliseconds(200);

    public const int WatchBatchSize = 32;

    public const int WatchQueueLimit = 4_096;

    private const int BackgroundIndexThreshold = 2_048;

    public IReadOnlyList<string> PlaceholderNames { get; private set; }

    public EntryStore? Store => _session?.Store;

    public EntryViewIndex? ViewIndex => _viewIndex;

    public Task WhenCurrentSessionCompletes { get; private set; }

    public Task WhenFolderReleased => _releasedFolder;

    public NavigationController Navigation => _navigation;
    public void RestoreNavigationHistory(NavigationHistoryState state)
    {
        AssertUi();
        _navigation.RestoreHistory(state);
        RaiseToolbar();
    }

    public void Navigate(string path)
    {
        AssertUi();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AddressText = path;
        ErrorText = null;
        RaiseToolbar();

        string normalized;
        try
        {
            normalized = _paths.Normalize(path);
        }
        catch (ArgumentException ex)
        {
            CancelIndexBuild();
            _ = _navigation.ParkCurrentForRecovery();
            ErrorText = ex.Message;
            IsLoading = false;
            StatusText = ex.Message;
            WhenCurrentSessionCompletes = Task.CompletedTask;
            _viewIndex = null;
            ItemCount = 0;
            PlaceholderNames = [];
            OnPropertyChanged(nameof(PlaceholderNames));
            OnPropertyChanged(nameof(ViewIndex));
            OnPropertyChanged(nameof(Store));
            _releasedFolder = DisposeCapturedAsync();
            RaiseToolbar();
            return;
        }

        if (TryOpenAsFile(normalized, out var parent))
        {
            normalized = parent;
        }

        AddressText = normalized;
        ResetFilters();
        var intent = _navigation.Open(normalized, recordHistory: true);
        StartSession(intent);
    }

    public void Back()
    {
        AssertUi();
        var intent = _navigation.Back();
        if (intent is null)
        {
            return;
        }

        AddressText = intent.Value.Path;
        ErrorText = null;
        ResetFilters();
        StartSession(intent.Value);
    }

    public void Forward()
    {
        AssertUi();
        var intent = _navigation.Forward();
        if (intent is null)
        {
            return;
        }

        AddressText = intent.Value.Path;
        ErrorText = null;
        ResetFilters();
        StartSession(intent.Value);
    }

    public void Up()
    {
        AssertUi();
        var intent = _navigation.Up();
        if (intent is null)
        {
            return;
        }

        AddressText = intent.Value.Path;
        ErrorText = null;
        ResetFilters();
        StartSession(intent.Value);
    }

    public void Refresh()
    {
        AssertUi();
        if (!string.IsNullOrEmpty(AddressText) && !HomeLocation.IsHome(AddressText) && !TagLocation.IsTag(AddressText))
            ExplorerPreferenceBridge.InvalidateFolderSizes(AddressText);
        RefreshSession(preserveSurface: false);
    }

    private void ResetFilters()
    {
        _filter = EntryFilter.None;
        _tagFilter = null;
        OnPropertyChanged(nameof(FilterQuery));
        OnPropertyChanged(nameof(TagFilterLabel));
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
        if (Path.IsPathRooted(entry.Name))
        {
            return entry.Name;
        }

        var root = _navigation.CurrentPath ?? AddressText;
        return string.IsNullOrEmpty(root) ? entry.Name : _paths.Combine(root, entry.Name);
    }

    private bool TryOpenAsFile(string path, out string parent)
    {
        parent = path;
        if (HomeLocation.IsHome(path) || TagLocation.IsTag(path)
            || !File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }

        OpenFileRequested?.Invoke(this, path);
        var folder = _paths.GetParent(path);
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        parent = folder;
        return true;
    }

    public void SetSortColumn(EntrySortColumn column)
    {
        AssertUi();
        _sort = _sort.Column == column
            ? _sort with { Ascending = !_sort.Ascending }
            : _sort with { Column = column, Ascending = true };
        OnPropertyChanged(nameof(Sort));
        RebuildIndex();
    }

    public void RestoreSort(EntrySort sort)
    {
        AssertUi();
        if (_sort == sort) return;
        _sort = sort;
        OnPropertyChanged(nameof(Sort));
        RebuildIndex();
    }

    public void SetFilterQuery(string query)
    {
        AssertUi();
        var previous = _filter.Query;
        _filter = _filter with { Query = query ?? string.Empty };
        OnPropertyChanged(nameof(FilterQuery));
        var count = Store?.Count ?? 0;
        if (EntryViewIndex.ShouldDebounce(count, previous, _filter.Query))
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ExplorerPreferenceBridge.FolderSizesChanged -= OnFolderSizesChanged;
        CancelWatchRefresh();
        ClearWatchQueue();
        var filter = Interlocked.Exchange(ref _filterCts, null);
        if (filter is not null)
        {
            await filter.CancelAsync().ConfigureAwait(false);
            filter.Dispose();
        }

        CancelIndexBuild();

        await DisposeCapturedAsync().ConfigureAwait(false);
    }

    private void RefreshSession(bool preserveSurface)
    {
        if (string.IsNullOrEmpty(AddressText))
        {
            return;
        }

        try
        {
            var normalized = _paths.Normalize(AddressText);
            AddressText = normalized;
            ErrorText = null;
            var intent = _navigation.CurrentPath is not null
                ? _navigation.Refresh()
                : _navigation.Open(normalized, recordHistory: true);
            if (intent is not null)
            {
                StartSession(intent.Value, preserveSurface);
            }
        }
        catch (ArgumentException ex)
        {
            ErrorText = ex.Message;
            StatusText = ex.Message;
            IsLoading = false;
        }
    }

    private void StartSession(NavigationIntent intent, bool preserveSurface = false)
    {
        if (HomeLocation.IsHome(intent.Path))
        {
            ShowHome();
            return;
        }

        IsLoading = true;
        if (!preserveSurface)
        {
            ItemCount = 0;
            _viewIndex = null;
            PlaceholderNames = [];
            OnPropertyChanged(nameof(PlaceholderNames));
            OnPropertyChanged(nameof(ViewIndex));
            OnPropertyChanged(nameof(Store));
            StatusText = "Loading…";
        }

        RaiseToolbar();
        CancelWatchRefresh();
        ClearWatchQueue();
        CancelIndexBuild();

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
        lock (_sessionGate)
        {
            _session = session;
            _listenCts = listen;
        }

        WhenCurrentSessionCompletes = session.WhenCompleted;
        _ = PumpAsync(session, listen.Token);
        _ = ListenForChangesAsync(request, listen.Token);
    }

    private void ShowHome()
    {
        CancelWatchRefresh();
        ClearWatchQueue();
        CancelIndexBuild();
        IsLoading = false;
        ErrorText = null;
        ItemCount = 1; // Home is a dashboard; never probe disks to update its status.
        _viewIndex = null;
        PlaceholderNames = [];
        StatusText = StringTable.Get("Home");
        WhenCurrentSessionCompletes = Task.CompletedTask;
        _releasedFolder = DisposeCapturedAsync();
        OnPropertyChanged(nameof(PlaceholderNames));
        OnPropertyChanged(nameof(ViewIndex));
        OnPropertyChanged(nameof(Store));
        RaiseToolbar();
    }

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

        var overflow = false;
        var mutated = false;
        var applied = 0;
        while (applied < WatchBatchSize && _watchQueue.TryDequeue(out var notice))
        {
            if (!_navigation.Allows(notice.Generation) || notice.Generation != session.Generation)
            {
                continue;
            }

            if (notice.Kind == DirectoryWatchKind.Overflow)
            {
                overflow = true;
                break;
            }

            mutated |= ApplyWatchNotice(session, notice);
            applied++;
        }

        if (overflow)
        {
            ClearWatchQueue();
            RefreshSession(preserveSurface: true);
            return;
        }

        if (mutated)
        {
            RebuildIndex();
        }

        if (!_watchQueue.IsEmpty)
        {
            _dispatcher.Post(ApplyWatchBatch);
        }
    }

    private static bool ApplyWatchNotice(DirectorySession session, DirectoryWatchNotification notice)
    {
        switch (notice.Kind)
        {
            case DirectoryWatchKind.Deleted:
                return !string.IsNullOrEmpty(notice.Name) && session.Store.RemoveByName(notice.Name);
            case DirectoryWatchKind.Renamed:
                if (string.IsNullOrEmpty(notice.Name))
                {
                    return !string.IsNullOrEmpty(notice.OldName) && session.Store.RemoveByName(notice.OldName);
                }

                if (LiveDirectoryEntry.TryRead(session.Path, notice.Name, session.Options, out var renamed))
                {
                    session.Store.Rename(
                        string.IsNullOrEmpty(notice.OldName) ? notice.Name : notice.OldName,
                        renamed);
                    return true;
                }

                return !string.IsNullOrEmpty(notice.OldName) && session.Store.RemoveByName(notice.OldName);
            case DirectoryWatchKind.Created:
            case DirectoryWatchKind.Modified:
                if (string.IsNullOrEmpty(notice.Name)
                    || !LiveDirectoryEntry.TryRead(session.Path, notice.Name, session.Options, out var entry))
                {
                    return false;
                }

                session.Store.Upsert(entry);
                return true;
            default:
                return false;
        }
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
        if (string.IsNullOrEmpty(path) || HomeLocation.IsHome(path) || TagLocation.IsTag(path))
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
            await foreach (var change in session.ReadChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_navigation.Allows(session.Generation))
                {
                    return;
                }

                BuildAndPublishIndex(session, change, cancellationToken);
            }

            if (_navigation.Allows(session.Generation))
            {
                BuildAndPublishIndex(
                    session,
                    new DirectorySessionChange(session.Store.Count, session.State, session.Error));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void RebuildViewIndex() => RebuildIndex();

    private ulong SizeOf(FileEntryCore entry)
    {
        if (entry.Kind != EntryKind.Directory || !ExplorerPreferenceBridge.ShowFolderSizes())
        {
            return entry.Size;
        }

        return ExplorerPreferenceBridge.TryFolderSize(FullPath(entry), out var size) ? size : 0;
    }

    private void OnFolderSizesChanged()
    {
        _dispatcher.Post(() =>
        {
            if (_disposed || _sort.Column != EntrySortColumn.Size || !ExplorerPreferenceBridge.ShowFolderSizes())
            {
                return;
            }

            _ = RebuildIndexDebouncedAsync();
        });
    }

    private void RebuildIndex()
    {
        DirectorySession? session;
        lock (_sessionGate)
        {
            session = _session;
        }

        if (session is null)
        {
            return;
        }

        BuildAndPublishIndex(
            session,
            new DirectorySessionChange(session.Store.Count, session.State, session.Error));
    }

    private void BuildAndPublishIndex(
        DirectorySession session,
        DirectorySessionChange change,
        CancellationToken sessionCancellation = default)
    {
        // Enumeration, filter edits and decoder completion can race with cancellation.
        lock (_indexBuildGate)
        {
            if (_disposed || !_navigation.Allows(session.Generation))
                return;
            BuildAndPublishIndexCore(session, change, sessionCancellation);
        }
    }

    private void BuildAndPublishIndexCore(
        DirectorySession session,
        DirectorySessionChange change,
        CancellationToken sessionCancellation)
    {
        CancelIndexBuild();

        var buildCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _indexBuildCts = buildCts;
        var buildVersion = Interlocked.Increment(ref _indexBuildVersion);
        var sort = _sort;
        var filter = _filter;
        var matchingTagIds = _tagFilter?.MatchingEntryIds;
        var token = buildCts.Token;

        if (session.Store.Count < BackgroundIndexThreshold)
        {
            try
            {
                var index = BuildIndex(session, sort, filter, matchingTagIds, token);
                Publish(session, index, change, buildVersion);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                CompleteIndexBuild(buildCts);
            }

            return;
        }

        _ = BuildIndexInBackgroundAsync(
            session,
            change,
            sort,
            filter,
            matchingTagIds,
            buildVersion,
            buildCts);
    }

    private async Task BuildIndexInBackgroundAsync(
        DirectorySession session,
        DirectorySessionChange change,
        EntrySort sort,
        EntryFilter filter,
        IReadOnlySet<int>? matchingTagIds,
        long buildVersion,
        CancellationTokenSource buildCts)
    {
        try
        {
            var index = await Task.Run(
                () => BuildIndex(session, sort, filter, matchingTagIds, buildCts.Token),
                buildCts.Token).ConfigureAwait(false);
            Publish(session, index, change, buildVersion);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Trace.TraceError("Directory index build failed: {0}", error);
        }
        finally
        {
            CompleteIndexBuild(buildCts);
        }
    }

    private EntryViewIndex BuildIndex(
        DirectorySession session,
        EntrySort sort,
        EntryFilter filter,
        IReadOnlySet<int>? matchingTagIds,
        CancellationToken cancellationToken)
    {
        Func<FileEntryCore, bool>? tagMatch = matchingTagIds is null
            ? null
            : entry => matchingTagIds.Contains(entry.Id);
        return EntryViewIndex.Build(
            session.Store,
            sort,
            filter,
            _names,
            session.Generation,
            tagMatch,
            cancellationToken,
            SizeOf);
    }

    private void CancelIndexBuild()
    {
        lock (_indexBuildGate)
        {
            var previous = _indexBuildCts;
            _indexBuildCts = null;
            Interlocked.Increment(ref _indexBuildVersion);
            previous?.Cancel();
        }
    }

    private void CompleteIndexBuild(CancellationTokenSource buildCts)
    {
        lock (_indexBuildGate)
        {
            if (ReferenceEquals(_indexBuildCts, buildCts))
                _indexBuildCts = null;
            buildCts.Dispose();
        }
    }

    private async Task RebuildIndexDebouncedAsync()
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        try
        {
            await Task.Delay(EntryViewIndex.DebounceDelay, cts.Token).ConfigureAwait(false);
            RebuildIndex();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Publish(
        DirectorySession session,
        EntryViewIndex index,
        DirectorySessionChange change,
        long buildVersion)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed
                || !_navigation.Allows(session.Generation)
                || buildVersion != Volatile.Read(ref _indexBuildVersion))
            {
                return;
            }

            _viewIndex = index;
            ItemCount = index.Count;
            PlaceholderNames = CaptureNames(session.Store, index);
            OnPropertyChanged(nameof(PlaceholderNames));
            OnPropertyChanged(nameof(ViewIndex));
            OnPropertyChanged(nameof(Store));

            if (change.Error is not null)
            {
                ErrorText = change.Error.Message;
            }

            var loading = change.State is DirectorySessionState.Loading or DirectorySessionState.Partial;
            IsLoading = loading && change.Error is null;
            StatusText = FormatStatus(change, index.Count);
            RaiseToolbar();
            if (!IsLoading && !_watchQueue.IsEmpty)
            {
                ApplyWatchBatch();
            }
        });
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
            names[i] = store[index[i]].Name;
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
