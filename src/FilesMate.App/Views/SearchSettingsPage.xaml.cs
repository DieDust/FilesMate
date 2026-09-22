using Loc = FilesMate.App.Localization.StringTable;
using System.Collections.ObjectModel;

using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.Storage.Pickers;

using WinRT.Interop;

namespace FilesMate.App.Views;

public sealed partial class SearchSettingsPage : UserControl
{
    private bool _syncingGlobal = true;
    private int _globalRevision;
    private CancellationTokenSource? _globalDelay;
    private bool _capturingHotkey;
    private void GlobalSearchToggle_Changed(object sender, RoutedEventArgs e) => QueueGlobalSave(false);
    private void GlobalSearchTray_Changed(object sender, SelectionChangedEventArgs e) => QueueGlobalSave(false);
    private void GlobalSearchHotkey_Changed(object sender, TextChangedEventArgs e) => QueueGlobalSave(true);
    private async void QueueGlobalSave(bool debounce)
    {
        if (_syncingGlobal) return;
        _globalDelay?.Cancel();
        var request = new CancellationTokenSource();
        _globalDelay = request;
        var revision = ++_globalRevision;
        try
        {
            if (debounce) await Task.Delay(450, request.Token);
            var settings = new FilesMate.Search.GlobalSearchSettings(GlobalSearchResident.IsOn, GlobalSearchHotkey.Text,
                GlobalSearchTrayAction.SelectedIndex == 1 ? "Files" : "Search", GlobalSearchStartup.IsOn, GlobalSearchPreview.IsOn);
            // Starting a host and registering a hotkey can take a second; say so instead of leaving the last message up.
            GlobalSearchSaveStatus.Text = Loc.Get("Applying");
            var reply = await GlobalSearchService.ApplyAsync(settings);
            if (revision != _globalRevision) return;
            GlobalSearchSaveStatus.Text = reply.Ok && string.IsNullOrEmpty(reply.Message) ? Loc.Get("SavedAutomatically") : reply.Message;
            var saved = FilesMate.Search.GlobalSearchConfiguration.Load();
            _syncingGlobal = true;
            GlobalSearchResident.IsOn = saved.Enabled;
            GlobalSearchStartup.IsOn = saved.StartAtLogin;
            GlobalSearchPreview.IsOn = saved.PreviewEnabled;
            GlobalSearchTrayAction.SelectedIndex = saved.TrayLeftAction == "Files" ? 1 : 0;
            // A hotkey the host rejected stays in the box so it can be corrected; the status line names the one in force.
            if (reply.Ok) GlobalSearchHotkey.Text = saved.Hotkey;
            _syncingGlobal = false;
            // A rejected change leaves the host as it was, so its state line stays; an accepted one describes the result.
            if (reply.Ok) ShowHostState(settings.Enabled ? reply : null, saved, hostKnownDown: !settings.Enabled);
        }
        catch (OperationCanceledException) { }
        finally { if (_globalDelay == request) _globalDelay = null; request.Dispose(); }
    }

    private async void GlobalSearchOpen_Click(object sender, RoutedEventArgs e)
    {
        GlobalSearchOpen.IsEnabled = false;
        GlobalSearchSaveStatus.Text = Loc.Get("Search_Starting");
        try
        {
            var reply = await GlobalSearchService.EnsureStartedAsync(show: true);
            GlobalSearchSaveStatus.Text = reply.Ok ? "" : reply.Message;
            ShowHostState(reply.Ok ? reply : null, FilesMate.Search.GlobalSearchConfiguration.Load(), hostKnownDown: false);
            if (!reply.Ok) _ = RefreshHostStateAsync(_globalRevision);
        }
        finally { GlobalSearchOpen.IsEnabled = true; }
    }

    /// <summary>Asks the resident host how it is doing and paints the answer; cheap when no host is running.</summary>
    private async Task RefreshHostStateAsync(int revision)
    {
        var saved = FilesMate.Search.GlobalSearchConfiguration.Load();
        var reply = saved.Enabled ? await GlobalSearchService.StatusAsync() : null;
        if (!IsLoaded || revision != _globalRevision) return;
        ShowHostState(reply, saved, hostKnownDown: reply is null);
    }

    private void ShowHostState(FilesMate.Search.SearchHostReply? host, FilesMate.Search.GlobalSearchSettings saved, bool hostKnownDown)
    {
        string text; string glyph; var muted = false;
        if (!saved.Enabled)
        {
            text = Loc.Get("Search_ResidencyOffHint"); glyph = "\uE7BA"; muted = true;
        }
        else if (host is null)
        {
            text = hostKnownDown ? Loc.Get("Search_NotRunningHint") : Loc.Get("Search_Detecting"); glyph = hostKnownDown ? "\uE7BA" : "\uEA3A"; muted = !hostKnownDown;
        }
        else if (host.HotkeyRegistered)
        {
            text = Loc.Format("Search_HotkeyRegistered", saved.Hotkey); glyph = "\uE73E";
        }
        else
        {
            text = Loc.Format("Search_HotkeyUnregistered", saved.Hotkey); glyph = "\uE7BA";
        }
        GlobalSearchStatus.Text = text;
        GlobalSearchStateIcon.Glyph = glyph;
        GlobalSearchStateIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            !muted && host is { HotkeyRegistered: true } ? "FilesMate.Selection.AccentBrush" : "FilesMate.Text.SecondaryBrush"];
        GlobalSearchStartupHint.Text = saved.StartAtLogin && saved.Enabled && FilesMate.Search.GlobalSearchStartup.IsDisabledByWindows()
            ? Loc.Get("Search_StartupDisabledByWindows")
            : Loc.Get("Search_StartupHint");
    }

    private void GlobalSearchCapture_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        App.IsShortcutCaptureActive = true;
        GlobalSearchCapture.Content = Loc.Get("Shortcut_PressCombination");
        GlobalSearchCapture.Focus(FocusState.Programmatic);
    }

    private void GlobalSearchCapture_LostFocus(object sender, RoutedEventArgs e) { if (_capturingHotkey) EndHotkeyCapture(); }

    private void GlobalSearchCapture_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        if (e.Key == Windows.System.VirtualKey.Escape) { EndHotkeyCapture(); return; }
        if (e.Key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Menu or Windows.System.VirtualKey.Shift) return;
        static bool Down(Windows.System.VirtualKey key) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (Down(Windows.System.VirtualKey.LeftWindows) || Down(Windows.System.VirtualKey.RightWindows)) { GlobalSearchCapture.Content = Loc.Get("Shortcut_WinUnsupported"); return; }
        var modifiers = (Down(Windows.System.VirtualKey.Menu) ? 1u : 0u) | (Down(Windows.System.VirtualKey.Control) ? 2u : 0u) | (Down(Windows.System.VirtualKey.Shift) ? 4u : 0u);
        var shortcut = new FilesMate.Search.SearchHotkey(modifiers, (uint)e.Key);
        if (!FilesMate.Search.SearchHotkey.TryParse(shortcut.ToString(), out var parsed)) { GlobalSearchCapture.Content = Loc.Get("Shortcut_TryAnother"); return; }
        EndHotkeyCapture();
        GlobalSearchHotkey.Text = parsed.ToString();
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        App.IsShortcutCaptureActive = false;
        GlobalSearchCapture.Content = Loc.Get("Shortcut_Record");
    }

    private readonly SearchIndexSettingsService? _store;
    private SearchIndexSettings _settings;
    private IFileNameSearchIndex? _index;
    private bool _syncing;
    private bool _rankingChanged;
    private bool _relocating;
    private bool _readingUsage;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _usageTimer;
    private readonly ObservableCollection<SearchRankItem> _rankItems = [];

    public SearchSettingsPage()
    {
        InitializeComponent();
        var global = FilesMate.Search.GlobalSearchConfiguration.Load();
        GlobalSearchResident.IsOn = global.Enabled;
        GlobalSearchStartup.IsOn = global.StartAtLogin;
        GlobalSearchPreview.IsOn = global.PreviewEnabled;
        GlobalSearchHotkey.Text = global.Hotkey;
        GlobalSearchTrayAction.SelectedIndex = global.TrayLeftAction == "Files" ? 1 : 0;
        _syncingGlobal = false;
        _store = App.SearchIndexSettingsStore;
        _settings = _store?.Load() ?? SearchIndexSettings.Default;
        Heading.Text = StringTable.Get("SearchTitle");
        Lead.Text = StringTable.Get("SearchLead");
        IndexHeader.Text = StringTable.Get("SearchIndexSection");
        IndexCard.Title = StringTable.Get("SearchIndexTitle");
        IndexCard.Description = StringTable.Get("SearchIndexDescription");
        IndexButton.Content = StringTable.Get("SearchIndex");
        CancelButton.Content = StringTable.Get("SearchCancel");
        ProgressCard.Title = StringTable.Get("SearchProgress");
        DepthCard.Title = StringTable.Get("SearchDepth");
        DepthCard.Description = StringTable.Get("SearchDepthDescription");
        DepthUnlimitedItem.Content = StringTable.Get("SearchDepthUnlimited");
        IndexLocationCard.Title = StringTable.Get("SearchLocation");
        OpenLocationButton.Content = StringTable.Get("OpenDataFolder");
        ChangeLocationButton.Content = StringTable.Get("SearchChangeLocation");
        ParallelCard.Title = StringTable.Get("SearchParallel");
        ParallelCard.Description = StringTable.Get("SearchParallelDescription");
        AutoRefreshCard.Title = StringTable.Get("SearchAutoRefresh");
        AutoRefreshCard.Description = StringTable.Get("SearchAutoRefreshDescription");
        RankHeader.Text = StringTable.Get("SearchRankSection");
        RankLead.Text = StringTable.Get("SearchRankDescription");
        DrivesHeader.Text = StringTable.Get("SearchDrives");
        ExclusionsHeader.Text = StringTable.Get("SearchExclusions");
        ExclusionsLead.Text = StringTable.Get("SearchExclusionsDescription");
        StatFilesLabel.Text = StringTable.Get("SearchStatFiles");
        StatFoldersLabel.Text = StringTable.Get("SearchStatFolders");
        StatErrorsLabel.Text = StringTable.Get("SearchStatErrors");
        StatIndexedLabel.Text = StringTable.Get("SearchStatIndexed");
        BuildDrives();
        BuildRankList();
        EnsureDepthItem(_settings.MaxDepth);
        _syncing = true;
        ExclusionsBox.Text = string.Join(Environment.NewLine, _settings.Exclusions);
        ParallelToggle.IsOn = _settings.ScanInParallel;
        AutoRefreshToggle.IsOn = _settings.AutoRefresh;
        SelectDepth(_settings.MaxDepth);
        _syncing = false;
        ProgressText.Text = StringTable.Get("SearchIdle");
        Unloaded += (_, _) => { _usageTimer?.Stop(); if (_capturingHotkey) EndHotkeyCapture(); if (_globalDelay is not null) QueueGlobalSave(false); AttachIndex(null); };
        Loaded += (_, _) =>
        {
            AttachIndex(App.SearchIndex);
            _ = RefreshHostStateAsync(_globalRevision);
            _usageTimer ??= DispatcherQueue.CreateTimer();
            _usageTimer.Interval = TimeSpan.FromSeconds(3);
            _usageTimer.Tick -= UsageTimer_Tick;
            _usageTimer.Tick += UsageTimer_Tick;
            _usageTimer.Start();
            _ = RefreshUsageAsync();
            if (_index is FileNameIndexService { StorageError: { } error })
                ProgressText.Text = Loc.Format("SearchOperationFailed", error);
            else if (_index?.Stats.CompletedUtc is not null && !_index.IsRunning)
                ProgressText.Text = Loc.Get("SearchDone");
        };
    }

    private void BuildDrives()
    {
        DriveHost.Children.Clear();
        var selected = new HashSet<string>(_settings.Roots, StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            var box = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? root
                    : $"{drive.VolumeLabel} ({root.TrimEnd('\\')})",
                Tag = root,
                IsChecked = selected.Contains(root),
            };
            box.Checked += Drive_Changed;
            box.Unchecked += Drive_Changed;
            DriveHost.Children.Add(box);
        }
    }

    private void BuildRankList()
    {
        _rankItems.Clear();
        foreach (var kind in _settings.RankOrder)
        {
            _rankItems.Add(new SearchRankItem(kind, StringTable.Get(SearchHitKinds.TitleKey(kind))));
        }

        RankList.ItemsSource = _rankItems;
    }

    private async void RankList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        _rankingChanged = true;
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async void Drive_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async void ParallelToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async void AutoRefreshToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async void DepthBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private async void ExclusionsBox_LostFocus(object sender, RoutedEventArgs e) =>
        await SaveSettingsAsync().ConfigureAwait(true);

    private void IndexButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(() => _ = StartIndex()))
        {
            _ = StartIndex();
        }
    }

    private async Task StartIndex()
    {
        var index = App.SearchIndex;
        if (index is null || index.IsRunning || _relocating)
        {
            return;
        }

        if (!await SaveSettingsAsync().ConfigureAwait(true)) return;
        SetRunning(true);
        try
        {
            await index.RebuildAsync(_settings).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = Loc.Get("SearchIndexCancelled");
        }
        catch (Exception error)
        {
            ReportSettingsFailure("Building the search index", error);
        }
        finally
        {
            SetRunning(false);
            PaintStats(index.Stats);
            await RefreshUsageAsync();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => App.SearchIndex?.Cancel();

    private async void OpenLocationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _settings.DatabaseDirectory;
            Directory.CreateDirectory(path);
            _ = await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception error)
        {
            ReportSettingsFailure("Opening the search index folder", error);
        }
    }

    private async void ChangeLocationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ChangeLocationAsync();
        }
        catch (Exception error)
        {
            ReportSettingsFailure("Changing the search index folder", error);
        }
    }

    private async Task ChangeLocationAsync()
    {
        if (App.SearchIndex?.IsRunning == true || _relocating)
        {
            return;
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        if (App.CurrentWindow is { } window)
        {
            InitializeWithWindow.Initialize(picker, window.NativeHandle);
        }

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await ChangeLocationToAsync(folder.Path);
    }

    internal async Task ChangeLocationToAsync(string directory)
    {
        if (_relocating || App.SearchIndex?.IsRunning == true) return;
        _relocating = true;
        SetRunning(false);
        ProgressText.Text = Loc.Get("SearchRelocating");
        try
        {
            var result = await App.RelocateSearchIndexAsync(directory);
            ProgressText.Text = result.OldFilesRetained
                ? Loc.Format("SearchRelocatedRetained", _index?.FilePath ?? _settings.ResolveDatabasePath())
                : Loc.Get("SearchRelocated");
        }
        finally
        {
            _relocating = false;
            _settings = _store?.Load() ?? _settings;
            AttachIndex(App.SearchIndex);
            SetRunning(_index?.IsRunning == true);
            await RefreshUsageAsync();
        }
    }

    private void OnProgress(object? sender, SearchIndexProgress progress)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded || !ReferenceEquals(sender, _index)) return;
            SetRunning(progress.Running);
            PaintStats(new SearchIndexStats(progress.Files, progress.Folders, progress.Errors, progress.CompletedUtc ?? _index?.Stats.CompletedUtc));
            ProgressText.Text = progress.Running
                ? (progress.CurrentPath ?? StringTable.Get("SearchProgress"))
                : progress.Error is { } error ? Loc.Format("SearchOperationFailed", error)
                : Loc.Get(progress.Cancelled ? "SearchIndexCancelled" : "SearchDone");
            IndexProgress.IsIndeterminate = progress.Running;
        });
    }

    private void SetRunning(bool running)
    {
        IndexButton.IsEnabled = !running && !_relocating;
        ChangeLocationButton.IsEnabled = !running && !_relocating;
        CancelButton.IsEnabled = running && !_relocating;
        IndexProgress.IsIndeterminate = running || _relocating;
    }

    private void PaintStats(SearchIndexStats stats)
    {
        StatFilesValue.Text = stats.Files.ToString();
        StatFoldersValue.Text = stats.Folders.ToString();
        StatErrorsValue.Text = stats.Errors.ToString();
        StatIndexedValue.Text = stats.Indexed.ToString();
        StatCompletedValue.Text = stats.CompletedUtc?.ToLocalTime().ToString("g") ?? Loc.Get("SearchNotIndexed");
    }

    private void PaintLocation()
    {
        IndexLocationCard.Description = _index?.FilePath ?? _settings.ResolveDatabasePath();
    }

    private void AttachIndex(IFileNameSearchIndex? index)
    {
        if (ReferenceEquals(_index, index))
        {
            PaintLocation();
            return;
        }

        if (_index is not null)
        {
            _index.ProgressChanged -= OnProgress;
        }

        _index = index;
        if (_index is not null)
        {
            _index.ProgressChanged += OnProgress;
            SetRunning(_index.IsRunning);
            PaintStats(_index.Stats);
        }

        PaintLocation();
    }

    private void UsageTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => _ = RefreshUsageAsync();

    internal async Task RefreshUsageAsync()
    {
        if (_readingUsage || !IsLoaded) return;
        if (!_relocating && App.SearchIndex is { } active && !ReferenceEquals(active, _index))
        {
            _settings = _store?.Load() ?? _settings;
            AttachIndex(active);
        }
        _readingUsage = true;
        var index = _index;
        var path = index?.FilePath ?? _settings.ResolveDatabasePath();
        var building = (index as FileNameIndexService)?.BuildingPath;
        try
        {
            var usage = await Task.Run(() => SearchIndexStorage.ReadUsage(path, building));
            if (IsLoaded && ReferenceEquals(index, _index))
                StatSizeValue.Text = usage.IsAvailable ? Navigation.DriveCapacity.FormatBytes(usage.Bytes) : Loc.Get("SearchSizeUnavailable");
        }
        finally { _readingUsage = false; }
    }

    private async Task<bool> SaveSettingsAsync()
    {
        var roots = DriveHost.Children.OfType<CheckBox>()
            .Where(box => box.IsChecked == true && box.Tag is string)
            .Select(box => (string)box.Tag)
            .ToArray();
        var exclusions = ExclusionsBox.Text.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries);
        _syncing = true;
        try
        {
            _settings = SearchIndexSettings.Sanitize(
                roots,
                exclusions,
                ParallelToggle.IsOn,
                SelectedMaxDepth(),
                _settings.DatabaseDirectory,
                AutoRefreshToggle.IsOn,
                _rankingChanged ? _rankItems.Select(item => item.Kind) : _store?.Load().RankOrder ?? _settings.RankOrder);
            if (_store is not null)
            {
                await _store.SaveAsync(_settings, updateRankOrder: _rankingChanged).ConfigureAwait(true);
                _settings = _store.Load();
                _rankingChanged = false;
                BuildRankList();
            }
            return true;
        }
        catch (Exception error)
        {
            ReportSettingsFailure("Saving search settings", error);
            return false;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ReportSettingsFailure(string operation, Exception error)
    {
        System.Diagnostics.Trace.TraceError("{0} failed: {1}", operation, error);
        ProgressText.Text = Loc.Format("SearchOperationFailed", error.Message);
        SetRunning(App.SearchIndex?.IsRunning == true);
    }

    private int SelectedMaxDepth()
    {
        if (DepthBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out var depth))
        {
            return depth;
        }

        return _settings.MaxDepth;
    }

    private void EnsureDepthItem(int depth)
    {
        var tag = depth.ToString();
        if (DepthBox.Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == tag))
        {
            return;
        }

        DepthBox.Items.Insert(1, new ComboBoxItem { Content = tag, Tag = tag });
    }

    private void SelectDepth(int depth)
    {
        var tag = depth.ToString();
        foreach (var item in DepthBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == tag)
            {
                DepthBox.SelectedItem = item;
                return;
            }
        }
    }
}

public sealed class SearchRankItem(SearchHitKind kind, string title)
{
    public SearchHitKind Kind { get; } = kind;

    public string Title { get; } = title;
}
