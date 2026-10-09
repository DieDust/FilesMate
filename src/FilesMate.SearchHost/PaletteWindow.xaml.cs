using Loc = FilesMate.App.Localization.StringTable;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FilesMate.Platform.Windows.Processes;
using FilesMate.Search;
using FilesMate.App.Models;

namespace FilesMate.SearchHost;

public partial class PaletteWindow : Window
{
    private readonly System.Collections.ObjectModel.ObservableCollection<RankOption> _rankItems = new();
    private readonly SearchRankingPreferencesWatcher _rankWatcher;
    private readonly SearchHost _host;
    private readonly IGlobalSearchProvider _provider;
    private readonly SemaphoreSlim _queryGate = new(1, 1);
    private CancellationTokenSource? _query;
    private CancellationTokenSource? _iconQuery;
    private readonly SearchResultIcons _icons = new();
    private AppearanceSettings _appearance = AppearanceSettings.Default;
    private bool _ready;
    private bool _opening;
    private bool _composing;
    private bool _dismissed;
    private bool _pending;
    private SearchFilter _filter;
    private readonly System.Collections.ObjectModel.ObservableCollection<SearchRow> _rows = new();
    private readonly System.Collections.Generic.HashSet<SearchHitIdentity> _resultIdentities = new();
    private int _offset;
    private bool _hasMore;
    private bool _loadingMore;
    private bool _syncingSettings;
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private Native.Rect _workArea;
    private bool _centeringPalette;
    private DispatcherOperation? _queuedCenter;
    private bool _centerAfterGeneration;

    internal PaletteWindow(SearchHost host, IGlobalSearchProvider provider)
    {
        _host = host;
        _provider = provider;
        InitializeComponent();
        InitializeAppearanceTracking();
        RankingItems.ItemsSource = _rankItems;
        _rankWatcher = new SearchRankingPreferencesWatcher(Path.Combine(host.Profile, "search-index.json"),
            () => Dispatcher.BeginInvoke(new Action(RefreshSharedRanking), DispatcherPriority.Background));
        Activated += (_, _) => RefreshSharedRanking();
        Closed += (_, _) =>
        {
            CancelPaletteClose();
            CancelQueuedCenter();
            Results.ItemContainerGenerator.StatusChanged -= ResultsGenerator_StatusChanged;
            _rankWatcher.Dispose();
        };
        Unloaded += (_, _) => CancelQueuedCenter();
        Results.ItemContainerGenerator.StatusChanged += ResultsGenerator_StatusChanged;
        Results.ItemsSource = _rows;
        foreach (var panel in new FrameworkElement[] { SettingsPanel, RankingPanel, CategoriesPanel, HiddenResultsPanel })
            panel.IsVisibleChanged += (_, _) => { AnimateSection(panel); RefreshSearchPresentation(); if (panel.IsVisible) QueueCenter(); };
        LoadCategories();
        _previewSize = SearchPreviewSize.Load(host.Profile);
        _previewFocusTimer.Tick += (_, _) => CheckPreviewFocus();
        PreviewPopup.Opened += (_, _) => { ApplyPreviewRoundRegion(); _previewFocusTimer.Start(); };
        PreviewCard.SizeChanged += (_, _) => Dispatcher.BeginInvoke(new Action(ApplyPreviewRoundRegion), DispatcherPriority.Loaded);
        PreviewPopup.Closed += (_, _) => _previewFocusTimer.Stop();
        _settingsTimer.Tick += (_, _) => SaveSettings();
        _visibleIconsTimer.Tick += (_, _) => RefreshVisibleIcons();
        TextCompositionManager.AddPreviewTextInputStartHandler(QueryBox, Query_CompositionStarted);
        TextCompositionManager.AddPreviewTextInputHandler(QueryBox, Query_CompositionCompleted);
        SourceInitialized += (_, _) =>
        {
            ApplyNativeFrame();
        };
        SizeChanged += (_, e) => { if (IsVisible && (e.WidthChanged || e.HeightChanged)) QueueCenter(); QueueRoundRegion(); };
        DpiChanged += (_, e) =>
        {
            // Image DPI notifications also bubble here during container measure.
            // Only a DPI change of this window owns its viewport and placement.
            if (IsVisible && ReferenceEquals(e.OriginalSource, this)) QueueCenter();
        };
        Closing += (_, e) =>
        {
            CancelSearch();
            if (!_host.IsStopping) { e.Cancel = true; Dispatcher.BeginInvoke(new Action(() => Dismiss())); }
        };
        _ready = true;
    }


    internal void Prepare()
    {
        if (IsVisible) return;
        _appearance = EffectiveAppearance();
        PaletteAppearance.Apply(Resources, _appearance);
        new WindowInteropHelper(this).EnsureHandle();
        PanelBody.Measure(new Size(634, 590));
        PanelBody.Arrange(new Rect(PanelBody.DesiredSize));
        PanelBody.UpdateLayout();
        // Warm text, templates and software rendering without showing a window
        // or taking focus from the user's current application.
        var preview = new System.Windows.Media.Imaging.RenderTargetBitmap(660, 80, 96, 96, PixelFormats.Pbgra32);
        preview.Render(PanelBody);
    }

    internal void Open(bool settings, string notice)
    {
        LoadOpeningPreferences();
        _appearance = EffectiveAppearance();
        RefreshPaletteAppearance();
        if (_icons.UseBundledIcons != _appearance.UseBundledFileIcons)
        {
            CancelVisibleIcons();
            _icons.UseBundledIcons = _appearance.UseBundledFileIcons;
            foreach (var row in _rows) { row.Icon = _icons.Fallback(row.Hit); row.IconLoaded = false; }
        }
        _openVersion++;
        CancelPaletteClose();
        _closing = false;
        ResetPaletteMotion();
        if (IsVisible)
        {
            if (settings) ShowSettings(true, notice);
            RefreshSharedRanking();
            Activate();
            QueueVisibleIcons();
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            if (RankingPanel.IsVisible) RankingPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            else (SettingsPanel.IsVisible ? HotkeyBox : QueryBox).Focus();
            return;
        }
        _dismissed = false;
        _opening = true;
        _workArea = Native.ActiveWorkArea();
        _appearance = EffectiveAppearance();
        PaletteAppearance.Apply(Resources, _appearance);
        ApplyNativeFrame();
        ShowSettings(settings, notice);
        PreparePaletteOpen();
        Show();
        QueueVisibleIcons();
        Center();
        ApplyRoundRegion();
        Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        (settings ? HotkeyBox : QueryBox).Focus();
        _opening = false;
        AnimatePaletteOpen();
    }

    private bool _roundRegionQueued;
    private void QueueRoundRegion()
    {
        if (_roundRegionQueued) return;
        _roundRegionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _roundRegionQueued = false;
            ApplyRoundRegion();
        }));
    }

    private void QueueCenter()
    {
        if (!IsVisible || _dismissed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished
            || _queuedCenter is { Status: DispatcherOperationStatus.Pending }) return;
        // Loaded runs after the current render/layout pass. Multiple size/DPI
        // notifications share one operation instead of re-entering Measure.
        _queuedCenter = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _queuedCenter = null;
            if (IsVisible && !_dismissed && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished) Center();
        }));
    }

    private void CancelQueuedCenter()
    {
        _queuedCenter?.Abort();
        _queuedCenter = null;
        _centerAfterGeneration = false;
    }

    private void ResultsGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (!_centerAfterGeneration || Results.ItemContainerGenerator.Status == GeneratorStatus.GeneratingContainers) return;
        _centerAfterGeneration = false;
        QueueCenter();
    }

    private void Center()
    {
        if (Results.ItemContainerGenerator.Status == GeneratorStatus.GeneratingContainers)
        {
            // A direct caller may also run from a nested layout event. Wait for
            // its generator to finish without repeatedly queueing more work.
            _centerAfterGeneration = true;
            return;
        }
        var handle = new WindowInteropHelper(this).Handle;
        if (_centeringPalette || handle == IntPtr.Zero || _workArea.Right <= _workArea.Left || _workArea.Bottom <= _workArea.Top) return;
        _centeringPalette = true;
        try
        {
            var scale = VisualTreeHelper.GetDpi(this);
            Width = Math.Max(1, Math.Min(620, (_workArea.Right - _workArea.Left - 40) / scale.DpiScaleX));
            MaxHeight = Math.Max(1, Math.Min(500, (_workArea.Bottom - _workArea.Top - 40) / scale.DpiScaleY));
            var shellHeight = Shell.Padding.Top + Shell.Padding.Bottom + Shell.BorderThickness.Top + Shell.BorderThickness.Bottom;
            var contentWidth = Math.Max(1, Width - Shell.Padding.Left - Shell.Padding.Right - Shell.BorderThickness.Left - Shell.BorderThickness.Right);
            SearchHeader.Measure(new Size(contentWidth, double.PositiveInfinity));
            var pageHeight = Math.Max(1, MaxHeight - shellHeight - SearchHeader.DesiredSize.Height);
            SetPageViewport(CategoriesPanel, CategoriesPageScroll, CategoryEditorsScroll, 280, pageHeight, contentWidth);
            SetPageViewport(HiddenResultsPanel, HiddenResultsPageScroll, HiddenResultsList, 360, pageHeight, contentWidth);
            SetPageViewport(RankingPanel, RankingPageScroll, RankingScroll, 330, pageHeight, contentWidth);
            // One viewport stretches the cards and keeps the page scrollbar at its outer edge.
            SettingsPageScroll.MaxHeight = pageHeight;
            Results.MaxHeight = 336;
            if (SearchPanel.Visibility == Visibility.Visible)
            {
                SearchPanel.Measure(new Size(contentWidth, double.PositiveInfinity));
                var chrome = Math.Max(0, SearchPanel.DesiredSize.Height - Results.DesiredSize.Height);
                Results.MaxHeight = Math.Max(1, Math.Min(336, pageHeight - chrome));
            }
            if (!Native.GetWindowRect(handle, out var bounds)) return;
            var x = _workArea.Left + (_workArea.Right - _workArea.Left - (bounds.Right - bounds.Left)) / 2;
            // Keep the input anchored while result counts change; reserve space for
            // the expanded palette so it never grows beyond the monitor work area.
            var y = _workArea.Top + (int)((_workArea.Bottom - _workArea.Top - MaxHeight * scale.DpiScaleY) / 2);
            Native.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
        }
        finally { _centeringPalette = false; }
    }

    private static void SetPageViewport(FrameworkElement panel, ScrollViewer page, FrameworkElement viewport,
        double normalHeight, double availableHeight, double contentWidth)
    {
        page.MaxHeight = availableHeight;
        viewport.MaxHeight = normalHeight;
        if (panel.Visibility != Visibility.Visible) return;
        panel.Measure(new Size(contentWidth, double.PositiveInfinity));
        var chrome = Math.Max(0, panel.DesiredSize.Height - viewport.DesiredSize.Height);
        // Reserve a small inner viewport; the outer page scroll keeps its title,
        // add form and status reachable when their chrome exceeds the work area.
        viewport.MaxHeight = Math.Min(normalHeight, Math.Max(48, availableHeight - chrome));
    }

    public void CancelSearch(bool preservePreview = false)
    {
        ++_searchGeneration;
        _presentationCompleted = false;
        ResetSearchAnimations();
        _initialSourcesPending = false;
        _deferredSearchSnapshot = null;
        CancelSearchRenderObservation();
        CancelVisibleIcons();
        _pressedApplication = null;
        _pressedRow = null;
        _collapseSelectionOnUp = false;
        _openingClicks.CancelPendingClick();
        StopSearchActivity();
        if (preservePreview) CancelPreviewRead(); else ClearPreview();
        // Detach before cancelling: cancellation callbacks may re-enter this window while they run.
        var query = _query; _query = null;
        var icons = _iconQuery; _iconQuery = null;
        query?.Cancel();
        RetireQuery(icons);
        _loadingMore = false;
    }
    internal void Dismiss(bool notifyHost = true)
    {
        if (_dismissed) return;
        _dismissed = true;
        CancelPaletteClose();
        CancelQueuedCenter();
        Hide();
        ResetPaletteMotion();
        ResetSearchMotion();
        CancelSearch();
        ReleaseTextPreview();
        FlushSettings();
        _ready = false;
        QueryBox.Clear();
        ActiveQuery = string.Empty;
        _categoryId = _categories.First(c => c.Visible).Id;
        RenderCategoryButtons();
        _offset = 0;
        _hasMore = false;
        _pending = false;
        _presentationFailed = false;
        _presentationCompleted = false;
        _ready = true;
        _rows.Clear(); _resultIdentities.Clear();
        _visibleIconRows.Clear();
        _icons.ClearDynamicCache();
        CountLabel.Text = "";
        StatusText.Text = "";
        SearchPanel.Visibility = Visibility.Collapsed;
        ClearButton.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Visible;
        _composing = false;
        if (notifyHost && !_launching) _host.WindowDismissed();
    }
    private void Window_Deactivated(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(CheckPreviewFocus), DispatcherPriority.Background);
    private void Dismiss_Click(object sender, RoutedEventArgs e) => DismissAnimated();
    private void Query_CompositionStarted(object sender, TextCompositionEventArgs e)
    {
        _composing = true;
        CancelSearch();
        _pending = true;
        _presentationFailed = false;
        _hasMore = false;
        CountLabel.Text = Loc.Get("Search_Looking");
        StatusText.Text = "";
        StatusText.Visibility = IndexHelpButton.Visibility = Visibility.Collapsed;
        // Composition owns the input immediately, before its first TextChanged.
        // Stored rows are hidden and all commands reject their old generation.
        RefreshSearchPresentation();
    }

    private void Query_CompositionCompleted(object sender, TextCompositionEventArgs e)
    {
        if (!_composing) return;
        _composing = false;
        var generation = _searchGeneration;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // TextChanged may already have started the committed query, or a
            // newer composition may now own the input. Never search twice.
            if (generation == _searchGeneration && !_composing) Search();
        }), DispatcherPriority.Input);
    }

    private void Query_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ActiveQuery = QueryBox.Text.Trim();
        _offset = 0;
        _hasMore = false;
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = QueryBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (!_composing) Search(); else RefreshSearchPresentation();
    }

    private async void Search(bool append = false)
    {
        if (!_ready || !IsVisible || _composing || SettingsPanel.IsVisible || RankingPanel.IsVisible || CategoriesPanel.IsVisible || HiddenResultsPanel.IsVisible) return;
        if (append && (_pending || _initialSourcesPending || _deferredSearchSnapshot is not null || _loadingMore || !_hasMore)) return;
        var started = Stopwatch.GetTimestamp();
        if (!append)
        {
            CancelSearch();
            _iconQuery = new CancellationTokenSource();
            _offset = 0;
            _hasMore = false;
        }
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        _query = request;
        var generation = _searchGeneration;
        var query = QueryBox.Text.Trim();
        _presentationFailed = false;
        _presentationCompleted = false;
        var filter = _filter;
        var category = _categories.FirstOrDefault(c => c.Id == _categoryId);
        var provider = _provider is ConfiguredSearchProvider configured ? configured.ForCategory(_categoryId)
            : category is { Builtin: null } ? new IndexedFilesSearchProvider(_host.Profile, true, category.Extensions) : _provider;
        _loadingMore = append;
        _pending = !append;
        _initialSourcesPending = !append;
        TraceSearchStage(generation, started, append ? "page-requested" : "input-accepted");
        // Only the current query owns visible results. Stored old rows are hidden
        // until a current batch arrives; action guards also reject stale commands.
        IndexHelpButton.Visibility = Visibility.Collapsed;
        CountLabel.Text = append ? Loc.Format("Search_LoadingCount", _rows.Count) : Loc.Get("Search_Looking");
        StatusText.Text = query.Length == 0 ? "" : Loc.Get("Search_Looking");
        StatusText.Visibility = !append && _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSearchPresentation();
        if (!append) AnimateSearchLayout();
        if (query.Length > 0) _ = ShowSearchActivityAsync(request);
        try
        {
            if (query.Length == 0) { _rows.Clear(); _resultIdentities.Clear(); return; }
            if (!append) await Task.Delay(90, request.Token);
            TraceSearchStage(generation, started, "gate-waiting");
            await _queryGate.WaitAsync(request.Token);
            TraceSearchStage(generation, started, "gate-entered");
            var offset = _offset;
            try
            {
                if (!append && provider is IProgressiveGlobalSearchProvider progressive)
                {
                    var receivedFinal = false;
                    await Task.Run(async () =>
                    {
                        await foreach (var update in progressive.SearchUpdatesAsync(query, request.Token, filter, 40, 0).ConfigureAwait(false))
                        {
                            request.Token.ThrowIfCancellationRequested();
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (_query != request || generation != _searchGeneration || !IsVisible) return;
                                request.Token.ThrowIfCancellationRequested();
                                TraceSearchSources(generation, started, update);
                                receivedFinal |= update.IsFinal;
                                QueueSearchSnapshot(new(update.Response, update.IsFinal, update.IsIncomplete, filter, generation, started, false));
                            }, DispatcherPriority.Normal, request.Token).Task.ConfigureAwait(false);
                        }
                    }, request.Token);
                    if (!receivedFinal && _query == request && generation == _searchGeneration && IsVisible)
                        SearchFailed(false, Loc.Get("Search_PartialUnavailable"));
                }
                else
                {
                    var response = await Task.Run(() => provider.SearchAsync(query, request.Token, filter, 40, offset), request.Token);
                    if (_query != request || generation != _searchGeneration || !IsVisible) return;
                    QueueSearchSnapshot(new(response, true, false, filter, generation, started, append));
                }
            }
            finally { _queryGate.Release(); }
            if (append) await Dispatcher.Yield(DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            if (_query == request && IsVisible) SearchFailed(append, Loc.Get(!append && _acceptedSearchGeneration == generation && _rows.Count > 0
                ? "Search_PartialTimedOut" : "Search_SlowHint"));
        }
        catch (ArgumentException e)
        {
            if (_query == request && IsVisible) SearchFailed(append, Loc.Get(e.Message));
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            if (_query == request && IsVisible) SearchFailed(append, Loc.Get("Search_IndexUnavailable"));
        }
        finally
        {
            if (_query == request)
            {
                _query = null;
                _pending = _acceptedSearchGeneration != generation && _deferredSearchSnapshot is not null;
                _loadingMore = false;
                if (_deferredSearchSnapshot is null) _initialSourcesPending = false;
                StopSearchActivity();
                RefreshSearchPresentation();
            }
            request.Dispose();
        }
    }

    private void SearchFailed(bool append, string message)
    {
        _presentationFailed = true;
        _presentationCompleted = false;
        _deferredSearchSnapshot = null;
        _initialSourcesPending = false;
        if (!append) _hasMore = false;
        if (!append && _pending) ShowSearchError(message);
        else
        {
            CountLabel.Text = _rows.Count == 0 ? "" : Loc.Format(!append ? "SearchPage_Loaded" : _hasMore ? "Search_MoreCount" : "Search_ResultCount", _rows.Count);
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }
        RefreshSearchPresentation();
        AnimateSearchLayout();
    }
    private void Results_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueVisibleIcons();
        if (e.VerticalChange > 0 && e.OriginalSource is ScrollViewer scroll && scroll.ScrollableHeight - scroll.VerticalOffset < 180) Search(true);
    }
    private void Results_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Also permits retry when already at the bottom after a transient failure.
        if (e.Delta < 0 && FindChild<ScrollViewer>(Results) is { } scroll && scroll.ScrollableHeight - scroll.VerticalOffset < 180) Search(true);
    }

    private void ShowSearchError(string message)
    {
        _rows.Clear(); _resultIdentities.Clear();
        CountLabel.Text = "";
        StatusText.Text = message;
        StatusText.Visibility = IndexHelpButton.Visibility = Visibility.Visible;
        _presentationFailed = true;
        RefreshSearchPresentation();
    }
    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<SearchFilter>(tag, out var filter)) return;
        _filter = filter;
        _offset = 0;
        _hasMore = false;
        Search();
        QueryBox.Focus();
    }
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchRow row }) { Results.SelectedItem = row; CopySelectedPath(); QueryBox.Focus(); }
        e.Handled = true;
    }
    private void CopySelectedPath()
    {
        if (_pending || SelectedRows() is not { Length: > 0 } rows) return;
        if (rows.Any(row => !row.CanLocate)) { ActionError(Loc.Get("Search_ManagedAppHint")); return; }
        try { Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row => row.FilePath))); CountLabel.Text = Loc.Get("Path_Copied"); }
        catch (ExternalException) { CountLabel.Text = Loc.Get("Clipboard_BusyShort"); }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) { QueryBox.Clear(); QueryBox.Focus(); }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SearchRow row }) { Results.SelectedItem = row; OpenSelected(true); }
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_composing || key == Key.ImeProcessed) return;
        if (_resultMenu?.IsOpen == true) return;
        if (key == Key.Escape) { DismissAnimated(); e.Handled = true; return; }
        if (PreviewCard.IsKeyboardFocusWithin) return;
        if (SettingsPanel.Visibility == Visibility.Visible || RankingPanel.IsVisible || CategoriesPanel.IsVisible || HiddenResultsPanel.IsVisible) return;
        if (key == Key.Apps || key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift ||
            key == Key.Right && Keyboard.Modifiers == ModifierKeys.None && Results.IsKeyboardFocusWithin)
        { OpenResultMenu(true); e.Handled = true; return; }
        if (key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
        { InvokeFileCommand(SelectedRows().Length > 1 ? "BatchRename" : "Rename"); e.Handled = true; return; }
        if (key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None && !QueryBox.IsKeyboardFocusWithin)
        { InvokeFileCommand("Recycle"); e.Handled = true; return; }
        if (key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && (QueryBox.SelectionLength == 0 || !QueryBox.IsKeyboardFocusWithin)) { CopyFiles(); e.Handled = true; return; }
        if (key == Key.X && Keyboard.Modifiers == ModifierKeys.Control && (QueryBox.SelectionLength == 0 || !QueryBox.IsKeyboardFocusWithin)) { CopyFiles(true); e.Handled = true; return; }
        if (key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && !QueryBox.IsKeyboardFocusWithin) { Results.SelectAll(); e.Handled = true; return; }
        if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt) { ShowProperties(); e.Handled = true; return; }
        if (key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { CopySelectedPath(); e.Handled = true; return; }
        // Let focused action buttons handle their own Enter/Space semantics.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase) return;
        // Home/End belong to the caret while typing; PageUp/PageDown have no meaning in a one-line box, so they
        // always page the results. With focus in the list, the ListBox moves the selection itself.
        var caretKey = key is Key.Home or Key.End && QueryBox.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None;
        var navigationKey = key is Key.Down or Key.Up or Key.PageDown or Key.PageUp || key is Key.Home or Key.End && !caretKey;
        if (navigationKey && _pending) { e.Handled = true; return; }
        if (navigationKey && Results.IsKeyboardFocusWithin)
        {
            Dispatcher.BeginInvoke(() => QueuePreview(Results.SelectedItem as SearchRow), DispatcherPriority.Input);
            return;
        }
        if (navigationKey)
        {
            if (Results.Items.Count > 0)
            {
                var last = Results.Items.Count - 1;
                var page = Math.Max(1, VisibleResultRows() - 1);
                var current = Math.Max(0, Results.SelectedIndex);
                Results.SelectedIndex = key switch
                {
                    Key.Down => Math.Min(current + 1, last),
                    Key.Up => Math.Max(current - 1, 0),
                    Key.PageDown => Math.Min(current + page, last),
                    Key.PageUp => Math.Max(current - page, 0),
                    Key.End => last,
                    _ => 0,
                };
                Results.ScrollIntoView(Results.SelectedItem);
                QueuePreview(Results.SelectedItem as SearchRow);
                if (key is Key.PageDown or Key.End && _hasMore && Results.SelectedIndex == last) Search(true);
            }
            e.Handled = true;
        }
        if (key == Key.Enter) { OpenSelected((Keyboard.Modifiers & ModifierKeys.Control) != 0); e.Handled = true; }
    }

    /// <summary>How many result rows fit in the list right now; used as the PageUp/PageDown stride.</summary>
    private int VisibleResultRows()
    {
        if (Results.Items.Count == 0) return 8;
        var container = Results.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, Results.SelectedIndex)) as FrameworkElement
            ?? Results.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
        var rowHeight = container?.ActualHeight > 0 ? container.ActualHeight : 44;
        // The list scrolls by pixel (VirtualizingPanel.ScrollUnit="Pixel"), so the viewport is a height in DIPs.
        var viewport = FindChild<ScrollViewer>(Results)?.ViewportHeight ?? 0;
        var height = viewport > 0 ? viewport : Results.ActualHeight;
        return height > 0 ? Math.Max(1, (int)(height / rowHeight)) : 8;
    }
    private bool _launching;

    private async void OpenSelected(bool reveal)
    {
        if (_launching || _closing || _pending || SelectedRows() is not { Length: > 0 } rows) return;
        if (reveal && rows.Any(row => !row.CanLocate)) { ActionError(Loc.Get("Search_ManagedAppHint")); return; }
        var query = QueryBox.Text;
        var openVersion = _openVersion;
        _launching = true;
        ClearPreview();
        CountLabel.Text = Loc.Get("Opening");
        try
        {
            // Activation runs concurrently with the short closing transition.
            // Native shell delays cannot block rendering or keyboard input.
            var launch = ApplicationShell.RunAsync(() =>
            {
                if (rows.Any(row => !Exists(row))) throw new FileNotFoundException(Loc.Get("Search_StaleFiles"));
                foreach (var row in rows)
                {
                    if (reveal) _host.OpenManager(row.FilePath!, true);
                    else if (row.Hit.Application is { } app)
                    {
                        if (app.IsFilesMate) _host.OpenManager();
                        else ApplicationShell.Launch(app);
                    }
                    else if (row.Hit.IsDirectory) _host.OpenManager(row.Path);
                    else DetachedProcess.Open(row.Path);
                }
            });
            await AnimatePaletteCloseAsync(openVersion);
            await launch;
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException or System.Runtime.InteropServices.COMException
            or System.Runtime.InteropServices.InvalidComObjectException or ArgumentException or UnauthorizedAccessException)
        {
            if (openVersion == _openVersion)
            {
                if (!IsVisible) { Open(false, ""); QueryBox.Text = query; }
                ActionError(Loc.Get("Open_FailedPrefix") + e.Message);
            }
        }
        finally { _launching = false; if (!IsVisible) _host.WindowDismissed(); }
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        CancelSearch();
        FlushSettings();
        var show = SettingsPanel.Visibility != Visibility.Visible;
        ShowSettings(show, "");
        if (!show) { QueryBox.Focus(); Search(); }
    }
    private async void MoreResults_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _host.OpenSearchPage(new SearchPageRequest(QueryBox.Text, CategoryId: _categoryId, Sort: SearchSortConfiguration.Load(_host.Profile)));
            if (await AnimatePaletteCloseAsync(_openVersion)) _host.WindowDismissed();
        }
        catch (Exception error) { ActionError(error.Message); }
    }
    private void ShowSettings(bool show, string notice)
    {
        if (show) CancelSearch();
        RenderCategoryButtons();
        HiddenResultsPanel.Visibility = Visibility.Collapsed;
        RankingPanel.Visibility = Visibility.Collapsed;
        CategoriesPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SearchHeader.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        SearchPanel.Visibility = show || QueryBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _syncingSettings = true;
        ResidentBox.IsChecked = _host.Settings.Enabled;
        StartupBox.IsChecked = _host.Settings.StartAtLogin;
        PreviewEnabledBox.IsChecked = _host.Settings.PreviewEnabled;
        HotkeyBox.Text = _host.Settings.Hotkey;
        TrayFilesOption.IsChecked = _host.Settings.TrayLeftAction == "Files";
        TraySearchOption.IsChecked = !TrayFilesOption.IsChecked;
        _syncingSettings = false;
        SettingsStatus.Text = notice;
        if (!show && notice.Length > 0) { StatusText.Text = notice; StatusText.Visibility = Visibility.Visible; SearchPanel.Visibility = Visibility.Visible; }
        RefreshSearchPresentation();
    }
    private void Ranking_Click(object sender, RoutedEventArgs e)
    {
        LoadRanking(SearchRankingConfiguration.LoadPreferences(_host.Profile));
        SettingsPanel.Visibility = Visibility.Collapsed;
        RankingPanel.Visibility = Visibility.Visible;
        RankingStatus.Text = "";
    }
    private void LoadRanking(SearchRankingPreferences preferences)
    {
        _rankItems.Clear();
        foreach (var kind in preferences.RankOrder) _rankItems.Add(new(kind, preferences.IncludeStandaloneExecutables));
    }
    private string _sharedOrder = "";
    private void RefreshSharedRanking()
    {
        var signature = SearchSortConfiguration.Load(_host.Profile) + ":" + string.Join(",", SearchRankingConfiguration.Load(_host.Profile)) + ":" + SearchExecutableConfiguration.Load(_host.Profile);
        if (_sharedOrder != signature)
        {
            _sharedOrder = signature;
            if (_ready && IsVisible && !SettingsPanel.IsVisible && !RankingPanel.IsVisible && !CategoriesPanel.IsVisible) Search();
        }
        if (!RankingPanel.IsVisible || _dragging || _pressedRank is not null) return;
        var preferences = SearchRankingConfiguration.LoadPreferences(_host.Profile);
        if (_rankItems.Select(item => item.Kind).SequenceEqual(preferences.RankOrder)
            && _rankItems.FirstOrDefault(item => item.Kind == SearchHitKind.Executable)?.ExecutablesEnabled == preferences.IncludeStandaloneExecutables) return;
        LoadRanking(preferences);
    }
    private void RankExecutable_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || !RankingPanel.IsVisible || sender is not ToggleButton { DataContext: RankOption { Kind: SearchHitKind.Executable } } toggle ||
            toggle.IsChecked == SearchExecutableConfiguration.Load(_host.Profile)) return;
        try
        {
            SearchExecutableConfiguration.Save(toggle.IsChecked == true, _host.Profile);
            RenderCategoryButtons();
            RankingStatus.Text = Loc.Get("SavedAutomatically");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            toggle.IsChecked = SearchExecutableConfiguration.Load(_host.Profile);
            RankingStatus.Text = Loc.Get("Order_SaveFailed") + error.Message;
        }
    }
    private void RankingBack_Click(object sender, RoutedEventArgs e)
    { RankingPanel.Visibility = Visibility.Collapsed; SettingsPanel.Visibility = Visibility.Visible; }
    private void RankingReset_Click(object sender, RoutedEventArgs e)
    {
        _rankItems.Clear();
        var executablesEnabled = SearchRankingConfiguration.LoadPreferences(_host.Profile).IncludeStandaloneExecutables;
        foreach (var kind in SearchHitKinds.DefaultOrder) _rankItems.Add(new(kind, executablesEnabled));
        SaveRanking();
    }
    private void MoveRank(RankOption item, int direction)
    {
        var index = _rankItems.IndexOf(item);
        var next = index + direction;
        if (index >= 0 && next >= 0 && next < _rankItems.Count) { _rankItems.Move(index, next); SaveRanking(); }
    }
    private void SaveRanking()
    {
        try
        {
            SearchRankingConfiguration.Save(_rankItems.Select(item => item.Kind), _host.Profile);
            RankingStatus.Text = Loc.Get("SavedAutomatically");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            LoadRanking(SearchRankingConfiguration.LoadPreferences(_host.Profile));
            RankingStatus.Text = Loc.Get("Order_SaveFailed") + error.Message;
        }
    }
    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncingSettings || !SettingsPanel.IsVisible) return;
        SaveSettings();
    }
    private void Hotkey_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncingSettings || !SettingsPanel.IsVisible) return;
        _settingsTimer.Stop();
        _settingsTimer.Start();
    }
    private void Hotkey_LostFocus(object sender, RoutedEventArgs e) => FlushSettings();
    private void FlushSettings() { if (_settingsTimer.IsEnabled) SaveSettings(); }
    private void SaveSettings()
    {
        _settingsTimer.Stop();
        if (!_ready || _syncingSettings) return;
        var reply = _host.Apply(new(ResidentBox.IsChecked == true, HotkeyBox.Text, TrayFilesOption.IsChecked == true ? "Files" : "Search", StartupBox.IsChecked == true, PreviewEnabledBox.IsChecked == true));
        SettingsStatus.Text = reply.Ok && string.IsNullOrEmpty(reply.Message) ? Loc.Get("SavedAutomatically") : reply.Message;
        _syncingSettings = true;
        ResidentBox.IsChecked = _host.Settings.Enabled;
        StartupBox.IsChecked = _host.Settings.StartAtLogin;
        PreviewEnabledBox.IsChecked = _host.Settings.PreviewEnabled;
        TrayFilesOption.IsChecked = _host.Settings.TrayLeftAction == "Files";
        TraySearchOption.IsChecked = !TrayFilesOption.IsChecked;
        if (reply.Ok) HotkeyBox.Text = _host.Settings.Hotkey;
        _syncingSettings = false;
    }
    private void ManageIndex_Click(object sender, RoutedEventArgs e)
    {
        try { _host.OpenManager(searchSettings: true); Dismiss(); }
        catch (Exception error) when (error is IOException or Win32Exception)
        {
            SettingsStatus.Text = error.Message;
            if (!SettingsPanel.IsVisible) { StatusText.Text = error.Message; StatusText.Visibility = Visibility.Visible; }
        }
    }
    private void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift) { e.Handled = true; return; }
        if (modifiers == ModifierKeys.None) return;
        var text = ((modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "") + ((modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "") + ((modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") +
            (key == Key.Space ? "Space" : key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        if (SearchHotkey.TryParse(text, out var parsed)) HotkeyBox.Text = parsed.ToString();
        e.Handled = true;
    }
}
internal sealed record RankOption(SearchHitKind Kind, bool InitialEnabled)
{
    public bool ExecutablesEnabled { get; set; } = InitialEnabled;
    public Visibility ExecutableToggleVisibility => Kind == SearchHitKind.Executable ? Visibility.Visible : Visibility.Collapsed;
    public string Title => Kind switch
    {
        SearchHitKind.Shortcut => Loc.Get("SearchRankShortcut"), SearchHitKind.Program => Loc.Get("SearchRankProgram"), SearchHitKind.Executable => Loc.Get("SearchRankExecutable"), SearchHitKind.Folder => Loc.Get("Type_Folder"),
        SearchHitKind.Document => Loc.Get("Documents"), SearchHitKind.Image => Loc.Get("Pictures"), SearchHitKind.Video => Loc.Get("Videos"),
        SearchHitKind.Audio => Loc.Get("Audio"), SearchHitKind.Archive => Loc.Get("SearchRankArchive"), SearchHitKind.Code => Loc.Get("SearchRankCode"), _ => Loc.Get("SearchRankOther"),
    };
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    internal static Rect ActiveWorkArea()
    {
        var monitor = MonitorFromWindow(GetForegroundWindow(), 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfoW(monitor, ref info);
        return info.Work;
    }
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
