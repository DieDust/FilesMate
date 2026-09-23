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
    private readonly System.Collections.Generic.HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private int _offset;
    private bool _hasMore;
    private bool _loadingMore;
    private bool _syncingSettings;
    private readonly DispatcherTimer _settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private Native.Rect _workArea;

    internal PaletteWindow(SearchHost host, IGlobalSearchProvider provider)
    {
        _host = host;
        _provider = provider;
        InitializeComponent();
        RankingItems.ItemsSource = _rankItems;
        _rankWatcher = new SearchRankingPreferencesWatcher(Path.Combine(host.Profile, "search-index.json"),
            () => Dispatcher.BeginInvoke(new Action(RefreshSharedRanking), DispatcherPriority.Background));
        Activated += (_, _) => RefreshSharedRanking();
        Closed += (_, _) => _rankWatcher.Dispose();
        Results.ItemsSource = _rows;
        foreach (var panel in new FrameworkElement[] { SettingsPanel, RankingPanel, CategoriesPanel, HiddenResultsPanel })
            panel.IsVisibleChanged += (_, _) => AnimateSection(panel);
        LoadCategories();
        _previewSize = SearchPreviewSize.Load(host.Profile);
        _previewFocusTimer.Tick += (_, _) => CheckPreviewFocus();
        PreviewPopup.Opened += (_, _) => { ApplyPreviewRoundRegion(); _previewFocusTimer.Start(); };
        PreviewCard.SizeChanged += (_, _) => Dispatcher.BeginInvoke(new Action(ApplyPreviewRoundRegion), DispatcherPriority.Loaded);
        PreviewPopup.Closed += (_, _) => _previewFocusTimer.Stop();
        _settingsTimer.Tick += (_, _) => SaveSettings();
        _visibleIconsTimer.Tick += (_, _) => RefreshVisibleIcons();
        TextCompositionManager.AddPreviewTextInputStartHandler(QueryBox, (_, _) => { _composing = true; CancelSearch(); _pending = true; });
        TextCompositionManager.AddPreviewTextInputHandler(QueryBox, (_, _) => { _composing = false; Dispatcher.BeginInvoke(() => Search()); });
        SourceInitialized += (_, _) =>
        {
            ApplyNativeFrame();
        };
        SizeChanged += (_, e) => { if (IsVisible && e.WidthChanged) Center(); QueueRoundRegion(); };
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
        _appearance = PaletteAppearance.Load(_host.Profile);
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
        _appearance = PaletteAppearance.Load(_host.Profile);
        if (_icons.UseBundledIcons != _appearance.UseBundledFileIcons)
        {
            CancelVisibleIcons();
            _icons.UseBundledIcons = _appearance.UseBundledFileIcons;
            foreach (var row in _rows) { row.Icon = _icons.Fallback(row.Hit); row.IconLoaded = false; }
        }
        _openVersion++;
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
        _appearance = PaletteAppearance.Load(_host.Profile);
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

    private void ApplyNativeFrame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var rounded = 2; // Let DWM clip the acrylic and the window to the same rounded outline.
        Native.DwmSetWindowAttribute(hwnd, 33, ref rounded, sizeof(int));
        var dark = PaletteAppearance.IsDark(_appearance) ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var border = -2; // DWMWA_COLOR_NONE: the XAML border is the only outline.
        Native.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        var glass = PaletteAppearance.UseGlass(_appearance);
        var backdrop = glass ? 3 : 1; // DWMSBT_TRANSIENTWINDOW supplies actual desktop acrylic.
        var applied = Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) >= 0;
        var margins = new Native.Margins { Left = glass && applied ? -1 : 0 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        if (!glass || !applied) Resources["GlassSurface"] = Resources["Surface"];
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

    private void ApplyRoundRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect)) return;
        // A custom HRGN and DWM backdrop can have different corner silhouettes.
        // Native rounding clips both, and avoids allocating a region on each resize.
        Native.SetWindowRgn(hwnd, IntPtr.Zero, true);
        Shell.CornerRadius = new CornerRadius(8);
    }

    private void Center()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var scale = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(660, (_workArea.Right - _workArea.Left - 40) / scale.DpiScaleX);
        MaxHeight = Math.Min(590, (_workArea.Bottom - _workArea.Top - 40) / scale.DpiScaleY);
        Results.MaxHeight = Math.Max(60, Math.Min(372, MaxHeight - 170));
        SearchSettingsScroll.MaxHeight = Math.Max(140, MaxHeight - 110);
        Native.GetWindowRect(handle, out var bounds);
        var x = _workArea.Left + (_workArea.Right - _workArea.Left - (bounds.Right - bounds.Left)) / 2;
        // Keep the input anchored while result counts change; reserve space for
        // the expanded palette so it never grows beyond the monitor work area.
        var y = _workArea.Top + (int)((_workArea.Bottom - _workArea.Top - MaxHeight * scale.DpiScaleY) / 2);
        Native.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
    }

    public void CancelSearch(bool preservePreview = false)
    {
        CancelVisibleIcons();
        _pressedApplication = null;
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
        Hide();
        ResetSearchMotion();
        CancelSearch();
        ReleaseTextPreview();
        FlushSettings();
        _ready = false;
        QueryBox.Clear();
        _categoryId = _categories.First(c => c.Visible).Id;
        RenderCategoryButtons();
        _offset = 0;
        _hasMore = false;
        _pending = false;
        _ready = true;
        _rows.Clear(); _paths.Clear();
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
    private void Query_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _offset = 0;
        _hasMore = false;
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = QueryBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (!_composing) Search();
    }

    private async void Search(bool append = false)
    {
        if (!_ready || !IsVisible || _composing || SettingsPanel.IsVisible || RankingPanel.IsVisible || CategoriesPanel.IsVisible || HiddenResultsPanel.IsVisible) return;
        if (append && (_pending || _loadingMore || !_hasMore)) return;
        if (!append)
        {
            CancelSearch();
            _iconQuery = new CancellationTokenSource();
            _offset = 0;
            _hasMore = false;
        }
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        _query = request;
        var query = QueryBox.Text.Trim();
        var filter = _filter;
        var category = _categories.FirstOrDefault(c => c.Id == _categoryId);
        var provider = category is { Builtin: null } ? new IndexedFilesSearchProvider(_host.Profile, true, category.Extensions) : _provider;
        _loadingMore = append;
        _pending = !append;
        // Keep the previous frame visually stable while the next query runs.
        // Action handlers use _pending to reject stale opens/copies/drags, without
        // disabling the controls (which also dims their embedded buttons).
        IndexHelpButton.Visibility = Visibility.Collapsed;
        CountLabel.Text = append ? Loc.Format("Search_LoadingCount", _rows.Count) : Loc.Get("Search_Looking");
        SearchPanel.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        FilterBar.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = query.Length == 0 ? "" : Loc.Get("Search_Looking");
        StatusText.Visibility = !append && _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!append) AnimateSearchLayout();
        if (query.Length > 0) _ = ShowSearchActivityAsync(request);
        try
        {
            if (query.Length == 0) { _rows.Clear(); _paths.Clear(); return; }
            if (!append) await Task.Delay(90, request.Token);
            await _queryGate.WaitAsync(request.Token);
            GlobalSearchResponse response;
            var offset = _offset;
            try { response = await Task.Run(() => provider.SearchAsync(query, request.Token, filter, 40, offset), request.Token); }
            finally { _queryGate.Release(); }
            if (_query != request || !IsVisible) return;
            if (!append) { _rows.Clear(); _paths.Clear(); }
            var rows = response.Hits.Where(hit => _paths.Add(hit.Path)).Select(hit => new SearchRow(hit, _icons.Fallback(hit))).ToArray();
            var scroll = append ? FindChild<ScrollViewer>(Results) : null;
            var scrollOffset = scroll?.VerticalOffset ?? 0;
            foreach (var row in rows) _rows.Add(row);
            if (scroll is not null) scroll.ScrollToVerticalOffset(scrollOffset);
            _offset += response.Hits.Count;
            _hasMore = response.HasMore && response.Hits.Count > 0;
            if (!append)
            {
                Results.SelectedIndex = _rows.Count == 0 ? -1 : 0;
                if (Results.SelectedItem is not null) Results.ScrollIntoView(Results.SelectedItem);
            }
            CountLabel.Text = _rows.Count > 0 ? Loc.Format(_hasMore ? "Search_MoreCount" : "Search_ResultCount", _rows.Count) : "";
            StatusText.Text = response.Notice ?? (_rows.Count == 0 ? filter == SearchFilter.All
                ? Loc.Get("Search_NoResultsHint") : Loc.Get("Search_NoCategoryResults") : "");
            StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            IndexHelpButton.Visibility = response.Notice is not null || _rows.Count == 0 && filter == SearchFilter.All ? Visibility.Visible : Visibility.Collapsed;
            if (!append) { AnimateSearchLayout(); AnimateResultsChanged(); }
            QueueVisibleIcons();
            if (append) await Dispatcher.Yield(DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            if (_query == request && IsVisible) SearchFailed(append, Loc.Get("Search_SlowHint"));
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            if (_query == request && IsVisible) SearchFailed(append, Loc.Get("Search_IndexUnavailable"));
        }
        finally
        {
            if (_query == request) { _query = null; _pending = _loadingMore = false; StopSearchActivity(); }
            request.Dispose();
        }
    }

    private void SearchFailed(bool append, string message)
    {
        if (!append) ShowSearchError(message);
        else { CountLabel.Text = Loc.Format("Search_MoreCount", _rows.Count); StatusText.Text = message; StatusText.Visibility = Visibility.Visible; }
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
        _rows.Clear(); _paths.Clear();
        CountLabel.Text = "";
        StatusText.Text = message;
        StatusText.Visibility = IndexHelpButton.Visibility = Visibility.Visible;
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
    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_pending || e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        for (var node = source; node is not null && node != Results; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase) return;
        if (ItemsControl.ContainerFromElement(Results, source) is ListBoxItem { DataContext: SearchRow row })
        { Results.SelectedItem = row; OpenSelected(false); e.Handled = true; }
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
        if (!show && notice.Length > 0) { StatusText.Text = notice; SearchPanel.Visibility = Visibility.Visible; }
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
    private void RefreshSharedRanking()
    {
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
