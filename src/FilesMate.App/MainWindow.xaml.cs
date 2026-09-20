using Loc = FilesMate.App.Localization.StringTable;
using System.Diagnostics;
using System.Runtime.InteropServices;

using FilesMate.App.Animations;
using FilesMate.App.Diagnostics;
using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.App.Shortcuts;
using FilesMate.App.Theming;
using FilesMate.App.Views;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using Windows.System;

using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace FilesMate.App;

/// <summary>
/// Application window hosting navigator tabs.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _windowClosed;
    private const int SwMinimize = 6;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    private const string SettingsPageKey = "settings";
    private readonly IAppPageFactory _pageFactory;
    private readonly GlassSceneController _glassScene;
    private SettingsPage? _settingsPage;
    private Control? _settingsPreviousFocus;
    private string? _pendingSettingsSection;
    private bool _activatedMarked;
    private bool _nonClientUpdatePending;
    private bool _settingsLoadPending;
    private BackdropKind? _appliedBackdrop;
    private GlassEffectMode? _appliedGlass;
    private int? _appliedTransparency;
    private bool? _appliedSolidBackground;
    private readonly bool _hostTearOut;
    private bool _tabDragging;
    private bool _overTabTail;
    private bool _handledTabDrop;
    private static TabViewItem? DraggedTab;
    private static MainWindow? DragSource;
    private readonly WindowPlacementService _windowPlacement = new(Program.SettingsPath(WindowPlacementService.DefaultFilePath));
    private readonly WindowSessionStore _windowSession = new(Program.SettingsPath(WindowSessionStore.DefaultFilePath));
    private WindowPlacement _normalPlacement = WindowPlacement.Default;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _placementSaveTimer;
    private WindowPlacement? _savedPlacement;
    private bool _restoringPlacement;

    public bool IsHostTearOut => _hostTearOut;

    public MainWindow()
        : this(new AppPageFactory(), LaunchPath.Parse(Environment.GetCommandLineArgs()))
    {
    }

    internal MainWindow(IAppPageFactory pageFactory)
        : this(pageFactory, new LaunchTarget(null, null))
    {
    }

    internal MainWindow(IAppPageFactory pageFactory, LaunchTarget launch)
        : this(pageFactory, launch, hostTearOut: false)
    {
    }

    internal MainWindow(IAppPageFactory pageFactory, string? initialPath)
        : this(pageFactory, new LaunchTarget(initialPath, null))
    {
    }

    internal MainWindow(IAppPageFactory pageFactory, LaunchTarget launch, bool hostTearOut, WindowSession? restartSession = null)
    {
        _pageFactory = pageFactory ?? throw new ArgumentNullException(nameof(pageFactory));
        _hostTearOut = hostTearOut;
        InitializeComponent();
        InitializeTabMemory();
        InitializeShellCompatibility();
        ApplyShortcuts();
        App.ShortcutsChanged += App_ShortcutsChanged;

        var root = Content as FrameworkElement
            ?? throw new InvalidOperationException("The main window requires a FrameworkElement root.");
        root.ActualThemeChanged += Root_ActualThemeChanged;
        _glassScene = new GlassSceneController(this, root);
        Closed += MainWindow_Closed;
        AppWindow.Closing += (_, args) =>
        {
            if (_shellHost is null && !FileOperationLifetime.IsBusy) return;
            args.Cancel = true;
            DispatcherQueue.TryEnqueue(RequestCloseAfterFileWork);
        };

        if (_shellHost is null)
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SynchronizeTitleBarTheme();

        AppWindow.SetIcon("Assets/Branding/FilesMate.ico");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = hostTearOut ? 640 : 1024;
            presenter.PreferredMinimumHeight = hostTearOut ? 480 : 640;
        }

        // Apply saved bounds while the native window is still hidden. Restoring
        // from the first Activated event paints the default small window first,
        // which looks like the whole app is being stretched open.
        if (!hostTearOut)
        {
            RestorePlacement();
        }

        AppWindow.Changed += AppWindow_Changed;
        StartupClock.Mark("WindowConstructed");
        Activated += MainWindow_Activated;
        if (!hostTearOut)
        {
            InitializeTabs(launch, restartSession);
#if FILESMATE_UI_TEST
            if (Environment.GetEnvironmentVariable("FILESMATE_LANGUAGE_RESTART_SMOKE") == "1"
                || Environment.GetCommandLineArgs().Contains("--language-restart-smoke"))
                DispatcherQueue.TryEnqueue(async () => await RunLanguageRestartSmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_LOCALIZATION_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunLocalizationSmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_SHELL_TRANSPARENCY_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunShellTransparencySmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_PREVIEW_INTERACTION_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunPreviewInteractionSmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_UPDATE_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunUpdateSmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_NEW_TAB_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunNewTabSmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_CONVENIENCE_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunConvenienceSmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_TAB_MEMORY_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunTabMemorySmokeAsync());
            if (Environment.GetEnvironmentVariable("FILESMATE_SYSTEM_THEME_SMOKE") == "1")
                DispatcherQueue.TryEnqueue(async () => await RunSystemThemeSmokeAsync());
#endif
            if (launch.SettingsSection is { } section) DispatcherQueue.TryEnqueue(() => OpenSettings(section));
            if (launch.SearchAction is { } action) DispatcherQueue.TryEnqueue(() => _ = RunSearchActionAsync(action));
            if (launch.MissingPath is { } missing) DispatcherQueue.TryEnqueue(() => _ = ReportMissingLaunchPathAsync(missing));
        }
    }

    public void HandleLaunch(LaunchTarget target)
    {
        if (target.SearchAction is { } action) { _ = RunSearchActionAsync(action); return; }
        if (target.SettingsSection is { } section) { OpenSettings(section); return; }
        if (target.ActivateOnly) return;
        if (_hostTearOut) return;
        if (target.Folder is null && target.SelectPath is null)
        {
            if (target.MissingPath is { } missing) _ = ReportMissingLaunchPathAsync(missing);
            return;
        }

        // External activations (QQ, shell, jump list) always land in a new tab
        // inside the running app window. OpenFoldersInNewTab only governs in-app navigation.
        AddNavigatorTab(target.Folder, target.SelectPath);
        UpdateTaskbarTitle();
    }

    public WindowSession CaptureSession()
    {
        var tabs = new List<string>();
        foreach (var raw in Tabs.TabItems)
        {
            if (raw is TabViewItem item)
            {
                var path = TabPath(item);
                if (!string.IsNullOrEmpty(path))
                {
                    tabs.Add(path);
                }
            }
        }

        var selected = Tabs.SelectedItem is TabViewItem selectedTab
            ? Math.Max(0, Tabs.TabItems.IndexOf(selectedTab))
            : 0;
        return new WindowSession(tabs, selected);
    }

    private void InitializeTabs(LaunchTarget launch, WindowSession? restartSession)
    {
        if (restartSession is { Tabs.Count: > 0 })
        {
            RestoreSession(restartSession);
            return;
        }
        if (!string.IsNullOrEmpty(launch.Folder) || !string.IsNullOrEmpty(launch.SelectPath))
        {
            AddNavigatorTab(launch.Folder, launch.SelectPath);
            return;
        }

        if (App.ExplorerPreferences.RestoreLastSession)
        {
            var session = _windowSession.Load();
            if (session.Tabs.Count > 0)
            {
                RestoreSession(session);
                return;
            }
        }

        AddNavigatorTab(null);
    }

    private void RestoreSession(WindowSession session)
    {
        for (var i = 0; i < session.Tabs.Count; i++)
        {
            AddNavigatorTab(session.Tabs[i]);
        }

        if (session.SelectedTabIndex >= 0
            && session.SelectedTabIndex < Tabs.TabItems.Count
            && Tabs.TabItems[session.SelectedTabIndex] is TabViewItem selected)
        {
            Tabs.SelectedItem = selected;
        }

        UpdateTaskbarTitle();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_windowClosed || args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        OnWindowActivated();
    }

    private void OnWindowActivated()
    {
        if (_windowClosed) return;
        SynchronizeTitleBarTheme();
        App.NotifyActivated(this);
        if (_activatedMarked)
        {
            ScheduleNonClientRegionUpdate();
            return;
        }

        _activatedMarked = true;
        StartupClock.Mark("WindowActivated");
        ScheduleNonClientRegionUpdate();
    }

    public void SetFolderTab(object page, string name, string? glyph = null)
    {
        if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent tab }
            && ReferenceEquals(tab.Navigator, page))
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var item = (TabViewItem)Tabs.SelectedItem;
            ApplyHeader(item, name);
            ApplyTabIcon(item, glyph);
            UpdateTaskbarTitle(name);
        }
    }

    internal void UpdateTaskbarTitle(string? folderTitle = null)
    {
        if (_hostTearOut)
        {
            return;
        }

        if (string.IsNullOrEmpty(folderTitle)
            && Tabs.SelectedItem is TabViewItem selected)
        {
            folderTitle = selected.Header as string;
        }

        AppWindow.Title = string.IsNullOrEmpty(folderTitle)
            ? StringTable.Get("AppName")
            : folderTitle;
    }

    private readonly SemaphoreSlim _searchActionGate = new(1, 1);

    private async Task RunSearchActionAsync(string id)
    {
        await _searchActionGate.WaitAsync();
        try
        {
            var request = FilesMate.Search.SearchFileAction.Take(id);
            CloseSettings();
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (TabHost.Content is NavigatorPage { IsLoaded: true, XamlRoot: not null } page)
                {
                    await page.RunSearchActionAsync(request);
                    return;
                }
                await Task.Delay(100);
            }
            throw new InvalidOperationException(Loc.Get("Files_NotReady"));
        }
        catch (Exception error)
        {
            App.LogFailure("SearchFileAction", error);
            if (Content?.XamlRoot is { } root)
            {
                var dialog = new ContentDialog { XamlRoot = root, Title = Loc.Get("Files_ActionFailed"), Content = error.Message, CloseButtonText = Loc.Get("GlassEffectOff") };
                FilesMate.App.Theming.ContentDialogTheme.Apply(dialog, (FrameworkElement)Content);
                try { await dialog.ShowAsync(); }
                catch (Exception dialogError) { App.LogFailure("SearchFileActionDialog", dialogError); }
            }
        }
        finally { _searchActionGate.Release(); }
    }

    /// <summary>
    /// Somebody asked us to open a path that is gone (a stale search result, a shortcut, a shell verb). Activating
    /// the window and doing nothing reads as a bug, so name the path and say why nothing opened.
    /// </summary>
    private async Task ReportMissingLaunchPathAsync(string path)
    {
        for (var attempt = 0; attempt < 50 && Content?.XamlRoot is null; attempt++) await Task.Delay(100);
        if (Content?.XamlRoot is not { } root) return;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = StringTable.Get("NotFound_Title"),
            Content = new TextBlock { Text = path + Environment.NewLine + Environment.NewLine + StringTable.Get("NotFound_Body"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = StringTable.Get("Close"),
        };
        FilesMate.App.Theming.ContentDialogTheme.Apply(dialog, (FrameworkElement)Content);
        try { await dialog.ShowAsync(); }
        catch (Exception error) { App.LogFailure("MissingLaunchPathDialog", error); }
    }

    internal void ToggleTaskbarVisibility()
    {
        var hwnd = NativeHandle;
        AppLifecycle.TraceInternal($"Toggle executing hwnd=0x{hwnd.ToInt64():X} iconic={IsIconic(hwnd)}");
        if (IsIconic(hwnd))
        {
            AppLifecycle.TryBringToForeground(hwnd);
            Activate();
            AppLifecycle.TraceInternal("Toggle restored");
            return;
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }

        if (!IsIconic(hwnd))
        {
            _ = ShowWindow(hwnd, SwMinimize);
        }

        AppLifecycle.TraceInternal($"Toggle minimized={IsIconic(hwnd)}");
    }

    private void Tabs_Loading(FrameworkElement sender, object args) => SuppressTabStripEntrance();

    private void Tabs_Loaded(object sender, RoutedEventArgs e)
    {
        if (NewTabButton is { } add)
        {
            AutomationProperties.SetName(add, StringTable.Get("NewTab"));
            ToolTipService.SetToolTip(add, StringTable.Get("NewTabTooltip"));
        }

        if (Tabs.TabItems is IObservableVector<object> items)
        {
            items.VectorChanged += (_, _) =>
            {
                ScheduleNonClientRegionUpdate();
            };
        }

        ScheduleNonClientRegionUpdate();
        SuppressTabStripEntrance();
    }

    private void Tabs_PointerEntered(object sender, PointerRoutedEventArgs e) => ScheduleNonClientRegionUpdate();

    private void Tabs_SizeChanged(object sender, SizeChangedEventArgs e) => ScheduleNonClientRegionUpdate();

    private (Rect Button, Rect Strip, double Scale, int Inset)? _titleBarGeometry;

    private void TitleBar_LayoutUpdated(object? sender, object e)
    {
        if (_windowClosed || NewTabButton?.XamlRoot is not { } root || NewTabButton.ActualWidth <= 0 || IsIconic(NativeHandle)) return;
        var geometry = (
            NewTabButton.TransformToVisual(null).TransformBounds(new Rect(0, 0, NewTabButton.ActualWidth, NewTabButton.ActualHeight)),
            Tabs.TransformToVisual(null).TransformBounds(new Rect(0, 0, Tabs.ActualWidth, Tabs.ActualHeight)),
            root.RasterizationScale, AppWindow.TitleBar.RightInset);
        if (_titleBarGeometry == geometry) return;
        _titleBarGeometry = geometry;
        // Tab headers and the footer can move without changing the TabView size.
        // Publish after layout, before another click is mistaken for a caption drag.
        UpdateNonClientRegions();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_windowClosed) return;
        if (args.DidSizeChange)
        {
            ScheduleNonClientRegionUpdate();
        }

        if (!_hostTearOut && !_restoringPlacement && (args.DidSizeChange || args.DidPositionChange))
        {
            SchedulePlacementSave();
        }
    }

    private void RestorePlacement()
    {
        if (_hostTearOut || _restoringPlacement)
        {
            return;
        }

        _restoringPlacement = true;
        try
        {
            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var minWidth = _hostTearOut ? 640 : 1024;
            var minHeight = _hostTearOut ? 480 : 640;
            var fitted = WindowPlacementMath.Fit(
                _windowPlacement.Load(),
                work.X,
                work.Y,
                work.Width,
                work.Height,
                minWidth,
                minHeight);
            _normalPlacement = fitted with { Maximized = false };
            AppWindow.MoveAndResize(new RectInt32(fitted.X, fitted.Y, fitted.Width, fitted.Height));
            if (fitted.Maximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceError("Window placement restore failed: {0}", ex);
        }
        finally
        {
            _restoringPlacement = false;
        }
    }

    private void SchedulePlacementSave()
    {
        Memory.TabResourceReclaimer.NotifyActivity();
        CaptureNormalPlacement();
        _placementSaveTimer?.Stop();
        if (_shellHost?.IsMovingOrSizing == true) return;
        if (_placementSaveTimer is null)
        {
            _placementSaveTimer = DispatcherQueue.CreateTimer();
            _placementSaveTimer.IsRepeating = false;
            _placementSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
            _placementSaveTimer.Tick += (_, _) => PersistPlacement();
        }
        _placementSaveTimer.Start();
    }

    private void CaptureNormalPlacement()
    {
        if (_hostTearOut || _restoringPlacement || IsIconic(NativeHandle)) return;
        var maximized = AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Maximized;
        if (!maximized && AppWindow.Size.Width > 0 && AppWindow.Size.Height > 0)
        {
            _normalPlacement = new WindowPlacement(
                AppWindow.Position.X,
                AppWindow.Position.Y,
                AppWindow.Size.Width,
                AppWindow.Size.Height,
                false);
        }
    }

    private void PersistPlacement()
    {
        _placementSaveTimer?.Stop();
        if (_hostTearOut || _restoringPlacement || IsIconic(NativeHandle)) return;
        CaptureNormalPlacement();
        var placement = _normalPlacement with
        {
            Maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized },
        };
        if (placement == _savedPlacement) return;

        try
        {
            _windowPlacement.Save(placement);
            _savedPlacement = placement;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Window placement save failed: {0}", ex);
        }
    }

    private void NewTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.IsShortcutCaptureActive)
        {
            args.Handled = true;
            return;
        }

        AddHomeTab();
        args.Handled = true;
    }

    private void CloseTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.IsShortcutCaptureActive)
        {
            args.Handled = true;
            return;
        }

        if (Tabs.SelectedItem is TabViewItem tab)
        {
            CloseTab(tab);
        }

        args.Handled = true;
    }

    private void CloseSettingsAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.IsShortcutCaptureActive)
        {
            return;
        }

        if (SettingsOverlay.Visibility == Visibility.Visible)
        {
            CloseSettings();
            args.Handled = true;
        }
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => AddHomeTab();

    private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) => CloseTab(args.Tab);

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tabDragging)
        {
            return;
        }

        ShowSelectedPage();
        UpdateTaskbarTitle();
    }

    private void Tabs_TabDragStarting(TabView sender, TabViewTabDragStartingEventArgs args)
    {
        _tabDragging = true;
        _overTabTail = false;
        _handledTabDrop = false;
        DraggedTab = args.Tab;
        DragSource = this;
        args.Data.RequestedOperation = DataPackageOperation.Move;
        UpdateNonClientRegions();
    }

    private void Tabs_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        _tabDragging = false;
        _overTabTail = false;
        if (ReferenceEquals(DraggedTab, args.Tab))
        {
            DraggedTab = null;
            DragSource = null;
        }

        _handledTabDrop = false;
        UpdateNonClientRegions();
        ShowSelectedPage();
    }

    private void Tabs_TabDroppedOutside(TabView sender, TabViewTabDroppedOutsideEventArgs args)
    {
        if (_handledTabDrop)
        {
            return;
        }

        if (_overTabTail)
        {
            MoveTabToEnd(args.Tab);
            return;
        }

        TearOutToNewWindow(args.Tab);
    }

    private void Tabs_TabStripDragOver(object sender, DragEventArgs e)
    {
        if (DraggedTab is null || DragSource is null || ReferenceEquals(DragSource, this))
        {
            return;
        }

        AcceptTabDrag(e);
    }

    private void Tabs_TabStripDrop(object sender, DragEventArgs e)
    {
        if (DraggedTab is not { } tab || DragSource is not { } source || ReferenceEquals(source, this))
        {
            return;
        }

        _handledTabDrop = true;
        var index = TabInsertIndex(e);
        source.DetachTab(tab);
        AttachTab(tab, index);
        if (source.Tabs.TabItems.Count == 0)
        {
            _ = source.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, source.CloseIfEmpty);
        }
    }

    private void TabStripTail_DragOver(object sender, DragEventArgs e)
    {
        if (DraggedTab is null)
        {
            return;
        }

        _overTabTail = true;
        AcceptTabDrag(e);
    }

    private void TabStripTail_DragLeave(object sender, DragEventArgs e) => _overTabTail = false;

    private void TabStripTail_Drop(object sender, DragEventArgs e)
    {
        if (DraggedTab is not { } tab)
        {
            return;
        }

        MoveTabToEnd(tab);
    }

    private void TabHost_DragOver(object sender, DragEventArgs e)
    {
        if (DraggedTab is null)
        {
            return;
        }

        AcceptTabDrag(e);
    }

    private void TabHost_Drop(object sender, DragEventArgs e)
    {
        if (DraggedTab is not { } tab)
        {
            return;
        }

        TearOutToNewWindow(tab);
    }

    private static void AcceptTabDrag(DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.IsCaptionVisible = false;
        e.Handled = true;
    }

    private void MoveTabToEnd(TabViewItem tab)
    {
        _handledTabDrop = true;
        var index = Tabs.TabItems.IndexOf(tab);
        if (index < 0 || index == Tabs.TabItems.Count - 1)
        {
            return;
        }

        Tabs.TabItems.Remove(tab);
        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;
    }

    private int TabInsertIndex(DragEventArgs e)
    {
        var position = e.GetPosition(Tabs);
        for (var i = 0; i < Tabs.TabItems.Count; i++)
        {
            if (Tabs.TabItems[i] is not FrameworkElement item)
            {
                continue;
            }

            var bounds = item.TransformToVisual(Tabs)
                .TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            if (position.X < bounds.X + (bounds.Width / 2))
            {
                return i;
            }
        }

        return Tabs.TabItems.Count;
    }

    private void TearOutToNewWindow(TabViewItem tab)
    {
        if (_handledTabDrop || !Tabs.TabItems.Contains(tab))
        {
            return;
        }

        _handledTabDrop = true;
        var window = new MainWindow(_pageFactory, new LaunchTarget(null, null), hostTearOut: true);
        App.TrackWindow(window);
        DetachTab(tab);
        window.AttachTab(tab);
        ApplyHostAppearance(window);
        window.Activate();
        if (Tabs.TabItems.Count == 0)
        {
            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, CloseIfEmpty);
        }
    }

    private static void ApplyHostAppearance(MainWindow window)
    {
        if (App.AppearanceViewModel?.Current is { } settings)
        {
            window.ApplyAppearance(settings);
        }
    }

    private void CloseIfEmpty()
    {
        if (Tabs.TabItems.Count == 0)
        {
            RequestCloseAfterFileWork();
        }
    }

    private bool _closeAfterFileWork;

    private async void RequestCloseAfterFileWork()
    {
        if (_closeAfterFileWork || _windowClosed) return;
        _closeAfterFileWork = true;
        if (FileOperationLifetime.IsBusy) AppWindow.Title = Loc.Get("Files_ClosingAfterOperation");
        while (FileOperationLifetime.IsBusy) await FileOperationLifetime.WhenIdleAsync();
        if (!_windowClosed) Close();
    }

    private void DetachTab(TabViewItem tab)
    {
        if (tab.Tag is NavigatorTabContent navigator && ReferenceEquals(TabHost.Content, navigator.Content))
        {
            TabHost.Content = null;
        }

        Tabs.TabItems.Remove(tab);
        RefreshClosable();
        if (Tabs.TabItems.Count > 0)
        {
            ShowSelectedPage();
        }

        ScheduleNonClientRegionUpdate();
    }

    private void AttachTab(TabViewItem tab, int index = -1)
    {
        if (Tabs.TabItems.Contains(tab))
        {
            return;
        }

        if (index < 0 || index > Tabs.TabItems.Count)
        {
            Tabs.TabItems.Add(tab);
        }
        else
        {
            Tabs.TabItems.Insert(index, tab);
        }

        ApplyTabItemStyle(tab);
        Tabs.SelectedItem = tab;
        RefreshClosable();
        ShowSelectedPage();
        ScheduleNonClientRegionUpdate();
    }

    private void AddHomeTab() => AddNavigatorTab(HomeLocation.Uri);

    private readonly ClosedTabHistory _closedTabs = new();
    public void ReopenClosedTab()
    {
        if (_closedTabs.Pop() is not { } state) return;
        AddNavigatorTab(state.Left.Path);
        if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent content }) content.RestoreState = state;
    }
    private void ReopenTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.IsShortcutCaptureActive) return;
        ReopenClosedTab();
        args.Handled = true;
    }

    private void CloseTab(TabViewItem tab)
    {
        if (Tabs.TabItems.Count <= 1)
        {
            return;
        }

        NavigatorPage? page = null;
        if (tab.Tag is NavigatorTabContent navigator)
        {
            _closedTabs.Push(navigator.RestoreState ?? navigator.Navigator?.CaptureClosedTab()
                ?? new ClosedTabState(new(navigator.RequestedPath ?? HomeLocation.Uri,
                    new FolderViewSettings(true, 2, FilesMate.Core.Entries.EntrySort.Name), 0)));
            page = navigator.TakeNavigatorForDisposal();
        }

        Tabs.TabItems.Remove(tab);
        tab.Tag = null;
        RefreshClosable();
        ShowSelectedPage();
        if (page is not null)
        {
            _ = DisposeNavigatorAsync(page);
        }
    }

    private void AddNavigatorTab(string? path, string? selectPath = null)
    {
        if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(selectPath))
        {
            path = Path.GetDirectoryName(selectPath);
        }

        var start = NavigatorPage.ResolveInitialPath(path);
        var tab = new NavigatorTabContent(start, selectPath, CreateLoadingContent());
        var item = new TabViewItem
        {
            Tag = tab,
            IconSource = TabIcon(LocationCaption.Glyph(start)),
        };
        ApplyTabItemStyle(item);
        ApplyHeader(item, LocationCaption.Title(start));
        Tabs.TabItems.Add(item);
        PaintTabHeaders();
        Tabs.SelectedItem = item;
        RefreshClosable();
        ScheduleNavigatorLoad(item, tab);
    }

    private void ScheduleNavigatorLoad(TabViewItem item, NavigatorTabContent tab)
    {
        var priority = TabHost.Content is NavigatorPage
            ? DispatcherQueuePriority.Normal
            : DispatcherQueuePriority.Low;
        _ = DispatcherQueue.TryEnqueue(priority, () =>
        {
            if (!Tabs.TabItems.Contains(item) || !ReferenceEquals(Tabs.SelectedItem, item) || !tab.Load.TryBegin())
            {
                return;
            }

            try
            {
                var page = new NavigatorPage(tab.RequestedPath, tab.SelectPath);
                if (!Tabs.TabItems.Contains(item) || !tab.Load.TryComplete() || tab.IsDisposed)
                {
                    _ = DisposeNavigatorAsync(page);
                    return;
                }

                tab.Navigator = page;
                tab.Content = page;
                tab.IsHibernated = false;
                item.Opacity = 1;
                ToolTipService.SetToolTip(item, tab.RequestedPath);
                if (tab.RestoreState is { } restored)
                {
                    _ = RestoreNavigatorStateAsync(page, tab, restored);
                }
                if (ReferenceEquals(Tabs.SelectedItem, item))
                {
                    ShowSelectedPage();
                }
            }
            catch (Exception error)
            {
                App.LogFailure("NavigatorLoad", error);
                if (!tab.Load.TryFail(error))
                {
                    return;
                }

                tab.Content = new PageLoadErrorPage("navigator", error.ToString());
                if (ReferenceEquals(Tabs.SelectedItem, item))
                {
                    ShowSelectedPage();
                }
            }
        });
    }

    private static UIElement CreateLoadingContent() => new Grid
    {
        Children =
        {
            new ProgressRing
            {
                Width = 24,
                Height = 24,
                IsActive = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    public void ApplyAppearance(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Themes.ShellStyleResources.Apply(Application.Current.Resources, settings);
        if (Content is Microsoft.UI.Xaml.Controls.Panel root)
        {
            var theme = settings.Theme switch
            {
                AppThemeKind.Light => ElementTheme.Light,
                AppThemeKind.Dark => ElementTheme.Dark,
                _ => SystemThemeResolver.ResolveElementTheme(),
            };
            if (root.RequestedTheme != theme)
            {
                root.RequestedTheme = theme;
            }

            TitleBarGlass.RequestedTheme = theme == ElementTheme.Default
                ? (root.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
                : theme;
            AppTitleBar.RequestedTheme = TitleBarGlass.RequestedTheme;
            Tabs.RequestedTheme = TitleBarGlass.RequestedTheme;

            var solidBackground = settings.Backdrop == BackdropKind.Solid
                || settings.GlassEffect == GlassEffectMode.Off || settings.EffectiveTransparencyPercent == 0;
            if (_appliedSolidBackground != solidBackground)
            {
                root.ClearValue(Panel.BackgroundProperty);
                root.Style = solidBackground
                    ? (Style)root.Resources["SolidShellRootStyle"]
                    : null;
                if (!solidBackground) root.Background = new SolidColorBrush(Colors.Transparent);
                _appliedSolidBackground = solidBackground;
            }
        }

        var effectiveBackdrop = settings.GlassEffect == GlassEffectMode.Off || settings.EffectiveTransparencyPercent == 0
            ? BackdropKind.Solid : settings.Backdrop;
        if (_appliedBackdrop != effectiveBackdrop)
        {
            SystemBackdrop = CreateBackdrop(effectiveBackdrop);
            _appliedBackdrop = effectiveBackdrop;
        }

        ApplyAccent(settings);
        SynchronizeOverlayTheme();
        if (_appliedGlass != settings.GlassEffect || _appliedTransparency != settings.EffectiveTransparencyPercent
            || _glassScene.State.SurfaceOpacity != Animations.GlassSceneState.Resolve(settings).SurfaceOpacity)
        {
            _glassScene.Apply(settings);
            _appliedGlass = settings.GlassEffect;
            _appliedTransparency = settings.EffectiveTransparencyPercent;
        }

        SynchronizeTitleBarTheme();
    }

    private static SystemBackdrop? CreateBackdrop(BackdropKind backdrop)
    {
        try
        {
            return backdrop switch
            {
                BackdropKind.Solid => null,
                BackdropKind.Mica => new MicaBackdrop { Kind = MicaKind.Base },
                BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                _ => new DesktopAcrylicBackdrop(),
            };
        }
        catch
        {
            return backdrop == BackdropKind.Solid ? null : new DesktopAcrylicBackdrop();
        }
    }

    private static void ApplyAccent(AppearanceSettings settings)
    {
        try
        {
            if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            {
                return;
            }
        }
        catch (Exception)
        {
        }

        PaintAccentTree(Application.Current.Resources, settings);
    }

    private static void PaintAccentTree(ResourceDictionary resources, AppearanceSettings settings)
    {
        foreach (var key in resources.ThemeDictionaries.Keys)
        {
            if (key is not string name
                || name.Equals("HighContrast", StringComparison.Ordinal)
                || resources.ThemeDictionaries[key] is not ResourceDictionary theme)
            {
                continue;
            }

            PaintAccentDictionary(theme, settings, dark: !name.Equals("Light", StringComparison.Ordinal));
        }

        foreach (var merged in resources.MergedDictionaries)
        {
            PaintAccentTree(merged, settings);
        }
    }

    private static void PaintAccentDictionary(ResourceDictionary dict, AppearanceSettings settings, bool dark)
    {
        var argb = AccentPalette.Resolve(settings.Accent, settings.CustomAccent, dark);
        var color = Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        var hover = Lift(color, 26);
        var soft = Color.FromArgb(dark ? (byte)0x47 : (byte)0x24, color.R, color.G, color.B);
        var selected = Color.FromArgb(dark ? (byte)0x47 : (byte)0x24, color.R, color.G, color.B);
        var selectedHover = Color.FromArgb(dark ? (byte)0x5C : (byte)0x30, color.R, color.G, color.B);
        var light1 = Lift(color, 26);
        var light2 = Lift(color, 48);
        var light3 = Lift(color, 72);
        var dark1 = Shade(color, 0.82);
        var dark2 = Shade(color, 0.64);
        var dark3 = Shade(color, 0.48);

        // Resolve only the accents we own/use. Enumerating every lazy WinUI
        // resource needlessly instantiates controls and can resolve material
        // aliases outside their active theme after backdrop switches.
        string[] names = ["FilesMate.Selection.AccentBrush", "FilesMate.Glass.AccentBrush",
            "FilesMate.Item.DropTargetBrush", "AccentFillColorDefaultBrush", "ToggleSwitchFillOn", "ToggleSwitchStrokeOn",
            "FilesMate.Glass.AccentHoverBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
            "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed",
            "FilesMate.Glass.AccentSoftBrush", "FilesMate.Item.SelectedBrush", "FilesMate.Item.SelectedHoverBrush",
            "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
            "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"];

        foreach (var name in names)
        {
            if (!dict.TryGetValue(name, out var value)) continue;

            switch (name)
            {
                case "FilesMate.Selection.AccentBrush":
                case "FilesMate.Glass.AccentBrush":
                case "FilesMate.Item.DropTargetBrush":
                case "AccentFillColorDefaultBrush":
                case "ToggleSwitchFillOn":
                case "ToggleSwitchStrokeOn":
                    AssignBrush(value, color);
                    break;
                case "FilesMate.Glass.AccentHoverBrush":
                case "AccentFillColorSecondaryBrush":
                case "AccentFillColorTertiaryBrush":
                case "ToggleSwitchFillOnPointerOver":
                case "ToggleSwitchFillOnPressed":
                case "ToggleSwitchStrokeOnPointerOver":
                case "ToggleSwitchStrokeOnPressed":
                    AssignBrush(value, hover);
                    break;
                case "FilesMate.Glass.AccentSoftBrush":
                    AssignBrush(value, soft);
                    break;
                case "FilesMate.Item.SelectedBrush":
                    AssignBrush(value, selected);
                    break;
                case "FilesMate.Item.SelectedHoverBrush":
                    AssignBrush(value, selectedHover);
                    break;
                case "SystemAccentColor" when value is Color:
                    dict[name] = color;
                    break;
                case "SystemAccentColorLight1" when value is Color:
                    dict[name] = light1;
                    break;
                case "SystemAccentColorLight2" when value is Color:
                    dict[name] = light2;
                    break;
                case "SystemAccentColorLight3" when value is Color:
                    dict[name] = light3;
                    break;
                case "SystemAccentColorDark1" when value is Color:
                    dict[name] = dark1;
                    break;
                case "SystemAccentColorDark2" when value is Color:
                    dict[name] = dark2;
                    break;
                case "SystemAccentColorDark3" when value is Color:
                    dict[name] = dark3;
                    break;
            }
        }
    }

    private static void AssignBrush(object value, Color color)
    {
        if (value is SolidColorBrush brush)
        {
            brush.Color = color;
        }
    }

    private static Color Lift(Color color, int delta) =>
        Color.FromArgb(
            color.A,
            (byte)Math.Min(255, color.R + delta),
            (byte)Math.Min(255, color.G + delta),
            (byte)Math.Min(255, color.B + delta));

    private static Color Shade(Color color, double factor) =>
        Color.FromArgb(
            color.A,
            (byte)Math.Clamp((int)(color.R * factor), 0, 255),
            (byte)Math.Clamp((int)(color.G * factor), 0, 255),
            (byte)Math.Clamp((int)(color.B * factor), 0, 255));

    private void MainWindow_Closed(object sender, WindowEventArgs args)
        => CleanupWindow();

    private void CleanupWindow()
    {
        if (_windowClosed) return;
        _windowClosed = true;
        AppWindow.Changed -= AppWindow_Changed;
        _placementSaveTimer?.Stop();
        _tabMemoryTimer?.Stop();
        CancelFileTabHover();
        App.ExplorerPreferencesChanged -= TabMemoryPreferencesChanged;
        Activated -= MainWindow_Activated;
        try
        {
            PersistPlacement();
            if (Application.Current is App app) app.SaveWindowSession(this);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        { App.LogFailure("SaveWindowOnClose", error); }

        App.ShortcutsChanged -= App_ShortcutsChanged;
        foreach (var raw in Tabs.TabItems)
        {
            if (raw is TabViewItem { Tag: NavigatorTabContent tab } item)
            {
                var page = tab.TakeNavigatorForDisposal();
                item.Tag = null;
                if (page is not null)
                {
                    _ = DisposeNavigatorAsync(page);
                }
            }
        }

        _glassScene.Dispose();
        _shellHost?.Dispose();
        _shellHost = null;
    }

    private async Task DisposeNavigatorAsync(NavigatorPage page)
    {
        try
        {
            var retiredResources = page.CaptureRetiredResources();
            await page.DisposeAsync();
            ScheduleTabResourceRelease(retiredResources);
        }
        catch (Exception error)
        {
            App.LogFailure("NavigatorDisposal", error);
#if FILESMATE_UI_TEST
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "tab-disposal-errors.log"), error + Environment.NewLine);
#endif
        }
    }

    private void App_ShortcutsChanged(object? sender, EventArgs e) => ApplyShortcuts();

    private void ApplyShortcuts()
    {
        ApplyShortcut(NewTabAccelerator, ShortcutAction.NewTab);
        ApplyShortcut(CloseTabAccelerator, ShortcutAction.CloseTab);
        ApplyShortcut(ReopenTabAccelerator, ShortcutAction.ReopenTab);
    }

    private static void ApplyShortcut(KeyboardAccelerator accelerator, ShortcutAction action)
    {
        var gesture = App.Shortcuts[action];
        accelerator.Key = (VirtualKey)(int)gesture.Key;
        accelerator.Modifiers = (VirtualKeyModifiers)(int)gesture.Modifiers;
    }

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        SynchronizeTitleBarTheme();
        SynchronizeOverlayTheme();
        if (App.AppearanceViewModel?.Current is { } settings)
        {
            ApplyAccent(settings);
        }
    }

    private void SynchronizeTitleBarTheme()
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        var dark = root.ActualTheme == ElementTheme.Dark;
        var chromeTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        TitleBarGlass.RequestedTheme = chromeTheme;
        AppTitleBar.RequestedTheme = chromeTheme;
        Tabs.RequestedTheme = chromeTheme;
        PaintTabHeaders();

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var foreground = dark
            ? Windows.UI.Color.FromArgb(228, 255, 255, 255)
            : Windows.UI.Color.FromArgb(228, 0, 0, 0);
        var inactiveForeground = dark
            ? Windows.UI.Color.FromArgb(160, 255, 255, 255)
            : Windows.UI.Color.FromArgb(145, 32, 32, 32);
        var hoverBackground = dark
            ? Windows.UI.Color.FromArgb(28, 255, 255, 255)
            : Windows.UI.Color.FromArgb(18, 0, 0, 0);
        var pressedBackground = dark
            ? Windows.UI.Color.FromArgb(46, 255, 255, 255)
            : Windows.UI.Color.FromArgb(30, 0, 0, 0);
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
        titleBar.ForegroundColor = foreground;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonInactiveBackgroundColor = transparent;
    }

    private void SynchronizeOverlayTheme()
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        SettingsPanel.RequestedTheme = root.ActualTheme;
        SettingsHost.RequestedTheme = root.ActualTheme;
    }

    public void OpenFolderInNewTab(string path) => AddNavigatorTab(path);

    public void OpenFolderInNewWindow(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (App.ExplorerPreferences.OpenInExistingWindow && Application.Current is App)
        {
            HandleLaunch(new LaunchTarget(path, null));
            Activate();
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "\"" + path + "\"",
            UseShellExecute = true,
        });
    }

    public void OpenSettings(string? section = null)
    {
        if (SettingsOverlay.Visibility == Visibility.Visible)
        {
            if (section is null)
            {
                CloseSettings();
                return;
            }

            _pendingSettingsSection = section;
            _settingsPage?.ShowSection(section);
            return;
        }

        _settingsPreviousFocus = Content?.XamlRoot is { } root
            ? FocusManager.GetFocusedElement(root) as Control
            : null;
        _pendingSettingsSection = section;
        SettingsOverlay.Visibility = Visibility.Visible;
        SynchronizeOverlayTheme();

        if (_settingsPage is not null)
        {
            if (section is not null)
            {
                _settingsPage.ShowSection(section);
            }

            _settingsPage.Focus(FocusState.Programmatic);
            return;
        }

        SettingsHost.Content = CreateLoadingContent();
        ScheduleSettingsLoad();
    }

    public void CloseSettings()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        SettingsOverlay.Visibility = Visibility.Collapsed;
        _pendingSettingsSection = null;
        var previousFocus = _settingsPreviousFocus;
        _settingsPreviousFocus = null;
        if (previousFocus is not null)
        {
            _ = DispatcherQueue.TryEnqueue(() => previousFocus.Focus(FocusState.Programmatic));
        }
    }

    private void ScheduleSettingsLoad()
    {
        if (_settingsLoadPending)
        {
            return;
        }

        _settingsLoadPending = true;
        _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var creation = _pageFactory.Create(() => new SettingsPage());
            UIElement? page = creation.Page;
            if (!creation.Succeeded)
            {
                var diagnostics = creation.Error ?? "Settings page construction failed without diagnostics.";
                System.Diagnostics.Trace.TraceError("Settings page creation failed: {0}", diagnostics);
                page = _pageFactory.Create(
                    () => new PageLoadErrorPage(SettingsPageKey, diagnostics)).Page;
            }

            if (page is null)
            {
                System.Diagnostics.Trace.TraceError("Settings error-page creation also failed.");
                SettingsHost.Content = null;
                _settingsLoadPending = false;
                return;
            }

            if (page is SettingsPage settings)
            {
                _settingsPage = settings;
                settings.CloseRequested += SettingsPage_CloseRequested;
                if (_pendingSettingsSection is { } section)
                {
                    settings.ShowSection(section);
                }
            }

            SettingsHost.Content = page;
            _settingsLoadPending = false;
            if (SettingsOverlay.Visibility == Visibility.Visible && page is Control control)
            {
                control.Focus(FocusState.Programmatic);
            }
        });
    }

    private void SettingsPage_CloseRequested(object? sender, EventArgs e) => CloseSettings();

    private void ShowSelectedPage()
    {
        if (Tabs.SelectedItem is not TabViewItem item)
        {
            return;
        }

        MarkSelectedTabActivity(item);
        if (item.Tag is NavigatorTabContent { Load.State: LazyTabLoadState.Pending } pending)
            ScheduleNavigatorLoad(item, pending);

        var content = item.Tag switch
        {
            NavigatorTabContent navigator => navigator.Content,
            UIElement page => page,
            _ => null,
        };
        if (content is null || ReferenceEquals(TabHost.Content, content))
        {
            return;
        }

        // ponytail: a new tab starts as a ProgressRing on acrylic. Swapping the
        // live page out for that placeholder is the white flash. Keep the current
        // explorer until the real page exists; the upgrade is a hidden host that
        // can load off-screen.
        if (TabHost.Content is NavigatorPage && content is not NavigatorPage and not PageLoadErrorPage)
        {
            return;
        }

        TabHost.Content = content;
    }

    private void RefreshClosable()
    {
        var closable = Tabs.TabItems.Count > 1;
        foreach (var raw in Tabs.TabItems)
        {
            if (raw is TabViewItem item)
            {
                item.IsClosable = closable;
            }
        }
    }

    private void UpdateNonClientRegions()
    {
        if (Content?.XamlRoot is not { } root
            || IsIconic(NativeHandle))
        {
            return;
        }
        var scale = root.RasterizationScale;
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return;
        }

        RasterizeBrandIcon();
        RasterizeTabFolderIcons();
        // Native caption metrics can be transiently invalid during state changes.
        var captionWidth = Math.Max(0, AppWindow.TitleBar.RightInset) / scale;
        if (double.IsFinite(captionWidth))
        {
            CaptionPad.Width = captionWidth;
        }

        var passthrough = new List<RectInt32>();
        // The list viewport already covers the visible tabs. Individual tab
        // bounds extend outside its clip when scrolled and would swallow the
        // caption drag area (and caption buttons) to the right of the strip.
        AddNonClientRect(passthrough, FindDescendant<ListView>(Tabs, static _ => true), scale);
        AddNonClientRect(passthrough, NewTabButton, scale);
        AddTabStripPassthrough(passthrough, scale);
        if (_tabDragging)
        {
            AddNonClientRect(passthrough, TabDragRegion, scale);
        }
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        if (_shellHost is not null)
        {
            // Window.SetTitleBar sets this for a regular XAML Window; an island host
            // owns its non-client geometry and must publish the caption explicitly.
            source.SetRegionRects(NonClientRegionKind.Caption,
                [new RectInt32(0, 0, Math.Max(1, (int)Math.Ceiling(AppTitleBar.ActualWidth * scale)),
                    Math.Max(1, (int)Math.Ceiling(AppTitleBar.ActualHeight * scale)))]);
        }
        source.SetRegionRects(NonClientRegionKind.Passthrough, [.. passthrough]);
    }

    private void AddTabStripPassthrough(List<RectInt32> regions, double scale)
    {
        if (Tabs.ActualWidth <= 0 || Tabs.ActualHeight <= 0 || TabDragRegion.ActualHeight <= 0)
        {
            return;
        }

        var tabsBounds = Tabs.TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, Tabs.ActualWidth, Tabs.ActualHeight));
        var dragBounds = TabDragRegion.TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, Math.Max(TabDragRegion.ActualWidth, 1), TabDragRegion.ActualHeight));
        var width = dragBounds.X - tabsBounds.X;
        if (width <= 0 || tabsBounds.Height <= 0)
        {
            return;
        }

        regions.Add(new RectInt32(
            (int)Math.Round(tabsBounds.X * scale),
            (int)Math.Round(tabsBounds.Y * scale),
            (int)Math.Round(width * scale),
            (int)Math.Round(tabsBounds.Height * scale)));
    }

    private void SuppressTabStripEntrance()
    {
        var list = FindDescendant<ListView>(Tabs, static _ => true);
        if (list is null)
        {
            return;
        }

        list.ItemContainerTransitions.Clear();
        list.Transitions.Clear();
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T candidate && match(candidate))
            {
                return candidate;
            }

            var nested = FindDescendant(child, match);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? start)
        where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ScheduleNonClientRegionUpdate()
    {
        if (_nonClientUpdatePending)
        {
            return;
        }

        _nonClientUpdatePending = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _nonClientUpdatePending = false;
            UpdateNonClientRegions();
        }))
        {
            _nonClientUpdatePending = false;
        }
    }

    private static void AddNonClientRect(List<RectInt32> regions, FrameworkElement? element, double scale)
    {
        if (element is null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var bounds = element.TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var width = (int)Math.Round(bounds.Width * scale);
        var height = (int)Math.Round(bounds.Height * scale);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        regions.Add(new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            width,
            height));
    }

    private void ApplyTabItemStyle(TabViewItem item)
    {
        ConfigureFileTabHover(item);
        item.RequestedTheme = Tabs.RequestedTheme == ElementTheme.Default
            ? (Tabs.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
            : Tabs.RequestedTheme;

        if (Application.Current.Resources.TryGetValue("FilesMateTabViewItemStyle", out var resource)
            && resource is Style style)
        {
            item.Style = style;
        }

        if (item.ContextFlyout is MenuFlyout existing)
        {
            existing.Opening -= TabFlyout_Opening;
            existing.Opening += TabFlyout_Opening;
            return;
        }

        var flyout = new MenuFlyout { AreOpenCloseAnimationsEnabled = false };
        if (TryStyle("FilesMate.MenuFlyoutPresenterStyle", out var presenter))
        {
            flyout.MenuFlyoutPresenterStyle = presenter;
        }

        flyout.Opening += TabFlyout_Opening;
        item.ContextFlyout = flyout;
    }

    private void TabFlyout_Opening(object? sender, object e)
    {
        if (sender is not MenuFlyout menu)
        {
            return;
        }

        var tab = menu.Target as TabViewItem ?? FindAncestor<TabViewItem>(menu.Target);
        if (tab is null)
        {
            return;
        }

        (App.WindowForElement(tab) ?? this).PopulateTabFlyout(menu, tab);
    }

    private void PopulateTabFlyout(MenuFlyout menu, TabViewItem tab)
    {
        menu.Items.Clear();
        var index = Tabs.TabItems.IndexOf(tab);
        AddTabFlyoutItem(menu, "Tab_Close", "\uE8BB", () => CloseTab(tab), "Ctrl+W");
        AddTabFlyoutItem(menu, "Tab_CloseOthers", "\uE89F", () => CloseOtherTabs(tab));
        var closeRight = AddTabFlyoutItem(menu, "Tab_CloseToTheRight", "\uE76C", () => CloseTabsToTheRight(tab));
        closeRight.IsEnabled = index >= 0 && index < Tabs.TabItems.Count - 1;
        AddTabFlyoutItem(menu, "Tab_Duplicate", "\uE8C8", () => DuplicateTab(tab));
        AddTabFlyoutItem(menu, "Tab_Reopen", "\uE777", ReopenClosedTab, App.Shortcuts[ShortcutAction.ReopenTab].DisplayText).IsEnabled = _closedTabs.Count > 0;
        AddTabMemoryMenu(menu, tab);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddTabFlyoutItem(menu, "Tab_MoveToNewWindow", "\uE8A7", () => TearOutToNewWindow(tab));
    }

    private MenuFlyoutItem AddTabFlyoutItem(
        MenuFlyout menu,
        string labelKey,
        string glyph,
        Action action,
        string? accelerator = null)
    {
        var item = new MenuFlyoutItem
        {
            Text = StringTable.Get(labelKey),
            Icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 16,
            },
        };
        if (accelerator is not null)
        {
            item.KeyboardAcceleratorTextOverride = accelerator;
        }

        if (TryStyle("FilesMate.MenuFlyoutItemStyle", out var style))
        {
            item.Style = style;
        }

        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private static bool TryStyle(string key, out Style style)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style found)
        {
            style = found;
            return true;
        }

        style = null!;
        return false;
    }

    private void CloseOtherTabs(TabViewItem keep)
    {
        foreach (var tab in Tabs.TabItems.OfType<TabViewItem>().Where(item => !ReferenceEquals(item, keep)).ToArray())
        {
            CloseTab(tab);
        }
    }

    private void CloseTabsToTheRight(TabViewItem tab)
    {
        var index = Tabs.TabItems.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        for (var i = Tabs.TabItems.Count - 1; i > index; i--)
        {
            if (Tabs.TabItems[i] is TabViewItem item)
            {
                CloseTab(item);
            }
        }
    }

    private void DuplicateTab(TabViewItem tab) => AddNavigatorTab(TabPath(tab));

    private static string? TabPath(TabViewItem tab)
    {
        if (tab.Tag is not NavigatorTabContent content)
        {
            return null;
        }

        var path = content.Navigator?.ViewModel.AddressText;
        return string.IsNullOrEmpty(path) ? content.RequestedPath : path;
    }

    private static void ApplyHeader(TabViewItem item, string text)
    {
        if (string.IsNullOrEmpty(text) || Equals(item.Header, text))
        {
            return;
        }

        item.Header = text;
        item.Foreground = TabInk(item);
        AutomationProperties.SetName(item, text);
        ToolTipService.SetToolTip(item, text);
    }

    private void PaintTabHeaders()
    {
        var theme = Tabs.RequestedTheme == ElementTheme.Default
            ? (Tabs.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)
            : Tabs.RequestedTheme;
        foreach (var item in Tabs.TabItems.OfType<TabViewItem>())
        {
            if (item.RequestedTheme != theme)
            {
                item.RequestedTheme = theme;
            }

            var ink = TabInk(item);
            item.Foreground = ink;
            if (item.Header is TextBlock header)
            {
                header.Foreground = ink;
                header.HighContrastAdjustment = ElementHighContrastAdjustment.None;
            }
        }
    }

    private static SolidColorBrush TabInk(FrameworkElement element)
    {
        var dark = element.RequestedTheme == ElementTheme.Dark
            || (element.RequestedTheme == ElementTheme.Default && element.ActualTheme == ElementTheme.Dark);
        var fallback = dark
            ? Windows.UI.Color.FromArgb(228, 255, 255, 255)
            : Windows.UI.Color.FromArgb(228, 0, 0, 0);
        if (Application.Current.Resources.TryGetValue("FilesMate.Text.PrimaryBrush", out var resource)
            && resource is SolidColorBrush themed)
        {
            var color = themed.Color;
            var resourceIsDarkInk = color.R < 80 && color.G < 80 && color.B < 80;
            if (dark == !resourceIsDarkInk)
            {
                return new SolidColorBrush(color);
            }
        }

        return new SolidColorBrush(fallback);
    }

    private void RasterizeBrandIcon()
    {
        var pixels = ShellIconBinder.RasterizePixelSize(Content?.XamlRoot, 20);
        if (BrandIcon.Source is SvgImageSource existing
            && existing.RasterizePixelWidth == pixels
            && existing.RasterizePixelHeight == pixels)
        {
            return;
        }

        BrandIcon.Source = new SvgImageSource(new Uri("ms-appx:///Assets/Branding/FilesMate.svg"))
        {
            RasterizePixelWidth = pixels,
            RasterizePixelHeight = pixels,
        };
    }

    private void RasterizeTabFolderIcons()
    {
        var pixels = ShellIconBinder.RasterizePixelSize(Content?.XamlRoot, 16);
        foreach (var raw in Tabs.TabItems)
        {
            if (raw is not TabViewItem item || item.IconSource is not ImageIconSource image)
            {
                continue;
            }

            if (image.ImageSource is SvgImageSource svg
                && svg.RasterizePixelWidth == pixels
                && svg.RasterizePixelHeight == pixels)
            {
                continue;
            }

            item.IconSource = TabIcon(null);
        }
    }

    private void ApplyTabIcon(TabViewItem item, string? glyph) =>
        item.IconSource = TabIcon(glyph);

    private IconSource TabIcon(string? glyph)
    {
        if (!string.IsNullOrEmpty(glyph))
        {
            return new FontIconSource
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 16,
            };
        }

        var pixels = ShellIconBinder.RasterizePixelSize(Content?.XamlRoot, 16);
        return new ImageIconSource
        {
            ImageSource = new SvgImageSource(new Uri(FileTypeIconCatalog.AssetUri(FileIconKind.Folder)))
            {
                RasterizePixelWidth = pixels,
                RasterizePixelHeight = pixels,
            },
        };
    }

    internal async Task VacateFoldersAsync(IReadOnlyList<string> paths)
    {
        foreach (var item in Tabs.TabItems.OfType<TabViewItem>())
        {
            if (item.Tag is NavigatorTabContent { Navigator: { } page })
            {
                await page.VacateFoldersAsync(paths).ConfigureAwait(true);
            }
        }
    }

    private sealed class NavigatorTabContent(string? requestedPath, string? selectPath, UIElement content)
    {
        public UIElement Content { get; set; } = content;

        public LazyTabLoadSession Load { get; } = new();

        public NavigatorPage? Navigator { get; set; }
        public ClosedTabState? RestoreState { get; set; }
        public bool IsHibernated { get; set; }
        public bool KeepAlive { get; set; }
        public bool WasSelected { get; set; }
        public long InactiveSince { get; set; } = Environment.TickCount64;

        public string? RequestedPath { get; } = requestedPath;

        public string? SelectPath { get; } = selectPath;

        public bool IsDisposed { get; private set; }

        public NavigatorPage? TakeNavigatorForDisposal()
        {
            if (IsDisposed)
            {
                return null;
            }

            IsDisposed = true;
            Load.Cancel();
            var navigator = Navigator;
            Navigator = null;
            Content = new Grid();
            return navigator;
        }
    }
}
