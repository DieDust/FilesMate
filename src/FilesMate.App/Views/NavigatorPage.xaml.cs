using Loc = FilesMate.App.Localization.StringTable;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Home;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Diagnostics;
using FilesMate.App.Input;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Shortcuts;
using FilesMate.App.Navigation;
using FilesMate.App.Theming;
using FilesMate.App.Threading;
using FilesMate.App.Workspace;
using FilesMate.Core.Entries;
using FilesMate.Core.Metadata;
using FilesMate.Platform.Windows.Directories;
using FilesMate.Platform.Windows.Locks;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Sorting;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.ApplicationModel.DataTransfer;
using Windows.System;

using UiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage : Page, IAsyncDisposable
{
    private readonly PaneViewModel _leftVm;
    private PaneViewModel? _rightVm;
    private FileDetailsSurface? _rightSurface;
    private FilePaneChrome? _rightChrome;
    private HomeDashboard? _rightHome;
    private HomeDashboard? _homeDashboard;
    private HomeDashboard HomeDashboard
    {
        get
        {
            if (_homeDashboard is not null) return _homeDashboard;
            _homeDashboard = new HomeDashboard { Visibility = Visibility.Collapsed, IsHitTestVisible = false, Opacity = 0 };
            _homeDashboard.PlaceChosen += HomeDashboard_PlaceChosen;
            _homeDashboard.PlaceActionRequested += HomeDashboard_PlaceActionRequested;
            HomeContainer.Content = _homeDashboard;
            HomeContainer.Visibility = Visibility.Visible;
            return _homeDashboard;
        }
    }
    private bool _rightActive;
    private bool _dualPane;
    private readonly PaneFileActions _fileActions;
    private readonly PinnedLocationStore _pinnedLocations = new(PinnedLocationStore.DefaultFilePath);
    private readonly Dictionary<string, IReadOnlyList<TagDefinition>> _tagCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tagLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _tagCacheOrder = new();
    private readonly object _tagCacheGate = new();
    private readonly SemaphoreSlim _tagLoadGate = new(2, 2);
    private CancellationTokenSource _tagLoadCts = new();
    private readonly Dictionary<long, string> _tagNames = [];
    private Task? _tagCatalogProbe;
    private bool? _hasTagDefinitions;
    private const int TagCacheCapacity = 512;
    private bool _previewVisible;
    private PreviewPane? _previewHost;
    private PreviewPane PreviewHost
    {
        get
        {
            if (_previewHost is not null) return _previewHost;
            _previewHost = new PreviewPane();
            _previewHost.CloseRequested += (_, _) => SetPreviewVisible(false);
            if (App.PreviewService is { } service) _previewHost.Attach(service);
            PreviewContainer.Content = _previewHost;
            return _previewHost;
        }
    }
    private bool _lastShowHiddenFiles;
    private bool _lastShowFolderSizes;
    private string? _startupPath;
    private string? _pendingSelectPath;
    private PaneViewModel? _pendingSelectPane;
    private bool _pendingCreatedItemRename;
    private int _selectAttempts;
    private bool _homeOverlay;
    private bool _rightHomeOverlay;
    private bool _paneResizing;
    private bool _previewResizing;
    private bool _chromeScheduled;
    private bool _selectionUiScheduled;
    private string? _loadedPreviewPath;
    private bool _appliedStartupLayout;
    private bool _disposed;
    private TaskCompletionSource<bool>? _lockOverlayClosed;

    public NavigatorPage(string? initialPath = null, string? selectPath = null)
    {
        ViewModel = CreatePaneViewModel();
        _leftVm = ViewModel;
        InitializeComponent();
        Favorites.CurrentFolder = () => ViewModel.Navigation.CurrentPath;
        Favorites.OpenRequested += Favorites_OpenRequested;
        ShellRoot.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(ShellRoot_PointerPressed),
            handledEventsToo: true);
        ApplySidebarWidth(App.ExplorerPreferences.SidebarWidth, save: false);
        ShellSplit.IsPaneOpen = !App.Features.SidebarCollapsed;
        ApplyShortcuts();
        App.ShortcutsChanged += App_ShortcutsChanged;
        App.TagsChanged += App_TagsChanged;
        App.FolderCoversChanged += FolderCoversChanged;
        App.FolderCustomizations.ViewSettingsChanged += FolderViewScopeChanged;
        Commands.ShelfDragOver += ShelfDragOver;
        Commands.ShelfDrop += ShelfDrop;
        ShellRoot.SizeChanged += (_, _) => PositionShelf();
        FilePaneRow.SizeChanged += (_, _) => ApplyPreviewWidth(App.ExplorerPreferences.PreviewWidth, save: false);
        _lastShowHiddenFiles = App.ExplorerPreferences.ShowHiddenFiles;
        _lastShowFolderSizes = App.ExplorerPreferences.ShowFolderSizes;
        App.ExplorerPreferencesChanged += ExplorerPreferencesChanged;
        App.FeaturesChanged += NavigationFeaturesChanged;
        _fileActions = new PaneFileActions(
            new WindowsLocalFileOperations(),
            SelectedPaths,
            () => ViewModel.AddressText,
            PrimarySelectedPath,
            message => ViewModel.ReportUserError(message),
            RefreshFilePanes,
            App.VacateFoldersAsync,
            this);
        _fileActions.NewItemCreated = path =>
        {
            _pendingSelectPath = path;
            _pendingSelectPane = ViewModel;
            _pendingCreatedItemRename = true;
            _selectAttempts = 0;
            // A new item must be visible even when the previous list was filtered.
            if (!string.IsNullOrEmpty(ViewModel.FilterQuery)) ViewModel.SetFilterQuery(string.Empty);
            TryApplyPendingSelection(ViewModel);
        };
        InitializeConvenience();
        AutomationProperties.SetName(PaneToggle, StringTable.Get("ShowNavigation"));
        ToolTipService.SetToolTip(PaneToggle, StringTable.Get("ShowNavigation"));
        FileSurface.ResolvePath = entry => _leftVm.FullPath(entry);
        FileSurface.ResolveFolder = () => _leftVm.Navigation.CurrentPath ?? _leftVm.AddressText;
        FileSurface.ResolveTags = entry => ResolveTags(_leftVm, entry);
        FileSurface.CreateTagPicker = CreateTagPickerPanel;
        Omni.ResolveTagName = id =>
        {
            if (_tagNames.TryGetValue(id, out var name))
            {
                return name;
            }

            return Sidebar.PlaceFor(TagLocation.Uri(id), exact: true)?.Label;
        };
        FileSurface.IsPinnedPath = path => WindowsNavigationSource.IsPinned(path, _pinnedLocations);
        FileSurface.PresentationChanged += (_, _) =>
        {
            if (!_rightActive)
            {
                RefreshLayoutChrome();
                PersistFolderView(_leftVm);
            }
        };
        FileSurface.TerminalRequested += (_, path) => OpenTerminalAt(path);
        ConfigureConvenienceSurface(FileSurface, false);
        FileSurface.CommandRequested += (_, id) =>
        {
            ActivateRight(false);
            RunFileCommand(id);
        };
        FileSurface.DropRequested = request => HandleFileDropAsync(request, _leftVm);
        PaneChrome.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => ActivateRight(false)),
            handledEventsToo: true);
        Clipboard.ContentChanged += Clipboard_ContentChanged;
        Unloaded += NavigatorPage_Unloaded;
        App.FolderHandlerChanged += FolderHandlerChanged;
#if FILESMATE_UI_TEST
        if (Environment.GetEnvironmentVariable("FILESMATE_STATUS_SMOKE") == "1")
            Loaded += async (_, _) => await RunStatusSmokeAsync();
#endif
        SyncCommandBar();
        PaneChrome.StatusText = ViewModel.StatusText;
        RefreshLayoutChrome();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.OpenFileRequested += ViewModel_OpenFileRequested;
        App.AppearanceChanged += OnAppearanceChanged;
        ApplyAppearance(App.AppearanceViewModel?.Current ?? AppearanceSettings.Default);
        SyncChrome();
        var start = ResolveInitialPath(initialPath);
        _pendingSelectPath = selectPath;
        _pendingSelectPane = _leftVm;
        _selectAttempts = 0;

        if (!string.IsNullOrEmpty(start))
        {
            _startupPath = start;
            Omni.Text = start;
            Sidebar.SelectPath(start);
            if (HomeLocation.IsHome(start))
            {
                ShowHomeSurface(_leftVm, bindFiles: false);
            }
            else
            {
                UpdateEmptyHint(_leftVm);
            }
        }
        else
        {
            UpdateEmptyHint(_leftVm);
        }
    }

    public PaneViewModel ViewModel { get; private set; }

    private void NavigatorPage_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelFolderStatusRequests();
        _shellWindow.Dispose();
        _tagLoadCts.Cancel();
        _previewHost?.CancelAndClear();
    }

    internal void ReleaseInactiveVisuals()
    {
        if (IsLoaded) return;
        FileSurface.ReleaseInactiveVisuals();
        _rightSurface?.ReleaseInactiveVisuals();
    }

    internal WeakReference[] CaptureRetiredResources() =>
        new[] { this, Content, FileSurface, _rightSurface }.OfType<object>()
            // The XAML projection can disappear before its finalizable COM owner.
            // Follow that owner through finalization so native teardown is not
            // mistaken for complete merely because the Page weak reference died.
            .SelectMany(resource => resource is WinRT.IWinRTObject native
                ? new object[] { resource, native.NativeObject } : [resource])
            .Select(resource => new WeakReference(resource, trackResurrection: true)).ToArray();

    internal bool IsMemoryReclamationBusy => !_disposed && (_leftVm.IsLoading || _rightVm?.IsLoading == true
        || FileSurface.IsMemoryReclamationBusy || _rightSurface?.IsMemoryReclamationBusy == true);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelFolderStatusRequests();
        _shellWindow.Dispose();
        ShelfCard.Visibility = Visibility.Collapsed;
        Unloaded -= NavigatorPage_Unloaded;
        App.FolderHandlerChanged -= FolderHandlerChanged;
        App.AppearanceChanged -= OnAppearanceChanged;
        App.ShortcutsChanged -= App_ShortcutsChanged;
        App.TagsChanged -= App_TagsChanged;
        App.ExplorerPreferencesChanged -= ExplorerPreferencesChanged;
        App.FeaturesChanged -= NavigationFeaturesChanged;
        Clipboard.ContentChanged -= Clipboard_ContentChanged;
        _leftVm.PropertyChanged -= ViewModel_PropertyChanged;
        App.FolderCoversChanged -= FolderCoversChanged;
        App.FolderCustomizations.ViewSettingsChanged -= FolderViewScopeChanged;
        _leftVm.OpenFileRequested -= ViewModel_OpenFileRequested;
        if (_rightVm is not null)
        {
            _rightVm.PropertyChanged -= ViewModel_PropertyChanged;
            _rightVm.OpenFileRequested -= ViewModel_OpenFileRequested;
        }

        FileSurface.ResolvePath = null;
        FileSurface.ResolveFolder = null;
        FileSurface.ResolveTags = null;
        FileSurface.IsPinnedPath = null;
        FileSurface.ReleaseResources();
        _rightSurface?.ReleaseResources();
        _previewHost?.CancelAndClear();
        HideLockOverlay();
        _homeDashboard?.ReleaseResources();
        _rightHome?.ReleaseResources();
        HomeContainer.Content = null;
        _homeDashboard = null;
        _rightHome = null;

        // This page will never be loaded again. Disconnect its native visual tree
        // and shortcuts now, instead of waiting for WinRT reference tracking.
        KeyboardAccelerators.Clear();
        Content = null;

        var tagLoads = _tagLoadCts;
        tagLoads.Cancel();
        _tagCatalogProbe = null;
        await _leftVm.DisposeAsync().ConfigureAwait(false);
        if (_rightVm is not null)
        {
            await _rightVm.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnAppearanceChanged(object? sender, AppearanceSettings settings) => ApplyAppearance(settings);

    private void ExplorerPreferencesChanged(object? sender, ExplorerPreferences preferences)
    {
        _leftVm.RestoreSort(_leftVm.Sort with { MixChineseAndLatin = preferences.MixChineseAndLatin });
        if (_rightVm is not null) _rightVm.RestoreSort(_rightVm.Sort with { MixChineseAndLatin = preferences.MixChineseAndLatin });
        // DefaultView is for new tabs. Column-width saves must not switch layout.
        FileSurface.RebindVisibleEntries();
        _rightSurface?.RebindVisibleEntries();

        if (preferences.ShowHiddenFiles != _lastShowHiddenFiles)
        {
            _lastShowHiddenFiles = preferences.ShowHiddenFiles;
            _leftVm.Refresh();
            _rightVm?.Refresh();
        }

        if (preferences.ShowFolderSizes != _lastShowFolderSizes)
        {
            _lastShowFolderSizes = preferences.ShowFolderSizes;
            _leftVm.RebuildViewIndex();
            _rightVm?.RebuildViewIndex();
            UpdateFolderStatus(_leftVm);
            if (_rightVm is not null) UpdateFolderStatus(_rightVm);
        }

        if (!_paneResizing)
        {
            ApplySidebarWidth(preferences.SidebarWidth, save: false);
        }

        if (!_previewResizing && _previewVisible)
        {
            ApplyPreviewWidth(preferences.PreviewWidth, save: false);
        }

        SetDualPane(preferences.DualPane, persist: false);
        ApplyAlphabetNavigationPolicy(preferences);
        Commands.SetFolderSizesActive(preferences.ShowFolderSizes);
    }

    private static FileLayoutKind ToFileLayout(FolderViewKind view) =>
        view == FolderViewKind.Details ? FileLayoutKind.Details : FileLayoutKind.Grid;

    private void ApplyAppearance(AppearanceSettings settings)
    {
        CommandBarRow.Visibility = settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        PaneChrome.ShowStatusBar = settings.ShowStatusBar;
        if (_rightChrome is not null)
        {
            _rightChrome.ShowStatusBar = settings.ShowStatusBar;
        }
    }

    private bool _widthStatesHooked;
    private bool _paneOpenPending;
    private string? _pendingPaneState;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_tagLoadCts.IsCancellationRequested)
        {
            ResetTagLoads();
        }

        if (!_appliedStartupLayout)
        {
            _appliedStartupLayout = true;
            FileSurface.SetLayout(ToFileLayout(App.ExplorerPreferences.DefaultView));
        }

        SetDualPane(App.ExplorerPreferences.DualPane, persist: false);
        UpdateFolderStatus(_leftVm);
        if (_rightVm is not null) UpdateFolderStatus(_rightVm);
        Commands.SetFolderSizesActive(App.ExplorerPreferences.ShowFolderSizes);
        var startPath = _startupPath;
        if (startPath is { Length: > 0 })
        {
            _startupPath = null;
            ScheduleNavigation(() => ViewModel.Navigate(startPath));
        }

        HookWidthStates();
        UpdateShellWindow();
        StartupClock.Mark("FirstFrame");
        StartupClock.ExportIfProfile();
#if FILESMATE_UI_TEST
        if (Environment.GetEnvironmentVariable("FILESMATE_POLISH_SWEEP") == "1" && !_polishSweepStarted)
        {
            _polishSweepStarted = true;
            _ = RunPolishSweepAsync();
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_ROW_HOVER_SMOKE") == "1" && !_rowHoverSmokeStarted)
        {
            _rowHoverSmokeStarted = true;
            _ = RunRowHoverSmokeAsync();
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_NAVIGATION_SMOKE") == "1" && !_navigationSmokeStarted)
        {
            _navigationSmokeStarted = true;
            _ = RunNavigationSmokeAsync();
        }
#endif
        if (!HomeLocation.IsHome(startPath) && !HomeLocation.IsHome(ViewModel.AddressText))
        {
            ActiveSurface.Focus(FocusState.Programmatic);
        }
    }

    private void HookWidthStates()
    {
        if (_widthStatesHooked)
        {
            return;
        }

        var group = VisualStateManager.GetVisualStateGroups(ShellRoot)
            .FirstOrDefault(item => item.Name == "WidthStates");
        if (group is null)
        {
            return;
        }

        group.CurrentStateChanged += (_, args) => SchedulePaneOpen(args.NewState?.Name);
        _widthStatesHooked = true;
        ApplyPaneOpen(group.CurrentState?.Name);
    }

    private void SchedulePaneOpen(string? stateName)
    {
        _pendingPaneState = stateName;
        if (_paneOpenPending)
        {
            return;
        }

        _paneOpenPending = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyPendingPaneOpen))
        {
            _paneOpenPending = false;
        }
    }

    private void ApplyPendingPaneOpen()
    {
        _paneOpenPending = false;
        if (!IsLoaded)
        {
            return;
        }

        ApplyPaneOpen(_pendingPaneState);
    }

    private bool _paneUserToggled;

    private void NavigationFeaturesChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => { if (!_disposed) ApplyPaneOpen(null); });

    private void ApplyPaneOpen(string? stateName)
    {
        _ = stateName;
        var width = XamlRoot?.Size.Width ?? 0;
        if (width < 1)
        {
            width = ActualWidth;
        }

        // ponytail: AdaptiveTrigger Narrow matches MinWindowWidth 0, so skip close until the window has a real size.
        var narrow = width >= 1 && width < 720;
        var wantOpen = !App.Features.SidebarCollapsed && (!narrow || _paneUserToggled);
        PaneToggle.Visibility = Visibility.Visible;
        PaneToggleIcon.Glyph = wantOpen ? "\uE76B" : "\uE76C";
        var label = wantOpen ? Loc.Get("Sidebar_Collapse") : Loc.Get("Sidebar_Expand");
        AutomationProperties.SetName(PaneToggle, label);
        ToolTipService.SetToolTip(PaneToggle, label);

        if (ShellSplit.IsPaneOpen != wantOpen)
        {
            ShellSplit.IsPaneOpen = wantOpen;
        }

        ApplySidebarWidth(App.ExplorerPreferences.SidebarWidth, save: false);
    }

    private void ApplySidebarWidth(double width, bool save)
    {
        var clamped = ExplorerPreferences.ClampSidebarWidth(width);
        ShellSplit.OpenPaneLength = clamped;
        Sidebar.IsCompact = ExplorerPreferences.SidebarIsCompact(clamped);
        if (save && Math.Abs(App.ExplorerPreferences.SidebarWidth - clamped) >= 1)
        {
            _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { SidebarWidth = clamped });
        }
    }

    private void PaneResize_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = DesktopCursors.SizeWestEast;

    private void PaneResize_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneResizing)
        {
            ProtectedCursor = null;
        }
    }

    private void PaneResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _paneResizing = true;
        ProtectedCursor = DesktopCursors.SizeWestEast;
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PaneResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneResizing)
        {
            return;
        }

        ApplySidebarWidth(e.GetCurrentPoint(ShellSplit).Position.X, save: false);
        e.Handled = true;
    }

    private void PaneResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneResizing)
        {
            return;
        }

        _paneResizing = false;
        ProtectedCursor = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        ApplySidebarWidth(ShellSplit.OpenPaneLength, save: true);
        e.Handled = true;
    }

    private void PaneResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_paneResizing)
        {
            return;
        }

        _paneResizing = false;
        ProtectedCursor = null;
        ApplySidebarWidth(ShellSplit.OpenPaneLength, save: true);
    }

    private void ApplyPreviewWidth(double width, bool save)
    {
        if (!_previewVisible)
        {
            return;
        }

        var clamped = ExplorerPreferences.ClampPreviewWidth(width);
        // A remembered wide preview must not consume the file list on a smaller window.
        var available = FilePaneRow.ActualWidth;
        var fitted = available > 0
            ? Math.Min(clamped, Math.Max(0, available - Math.Min(280, available / 2) - 6))
            : clamped;
        PreviewColumn.Width = new GridLength(fitted);
        PreviewSplitterColumn.Width = new GridLength(6);
        if (save && Math.Abs(App.ExplorerPreferences.PreviewWidth - clamped) >= 1)
        {
            _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { PreviewWidth = clamped });
        }
    }

    private void PreviewResize_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = DesktopCursors.SizeWestEast;

    private void PreviewResize_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewResizing)
        {
            ProtectedCursor = null;
        }
    }

    private void PreviewResize_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _previewResizing = true;
        ProtectedCursor = DesktopCursors.SizeWestEast;
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PreviewResize_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewResizing)
        {
            return;
        }

        ApplyPreviewWidth(FilePaneRow.ActualWidth - e.GetCurrentPoint(FilePaneRow).Position.X, save: false);
        e.Handled = true;
    }

    private void PreviewResize_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewResizing)
        {
            return;
        }

        _previewResizing = false;
        ProtectedCursor = null;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        ApplyPreviewWidth(PreviewColumn.Width.Value, save: true);
        e.Handled = true;
    }

    private void PreviewResize_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_previewResizing)
        {
            return;
        }

        _previewResizing = false;
        ProtectedCursor = null;
        ApplyPreviewWidth(PreviewColumn.Width.Value, save: true);
    }

    private void Toolbar_BackClicked(object sender, RoutedEventArgs e) =>
        ScheduleNavigation(ViewModel.Back);

    private void Toolbar_ForwardClicked(object sender, RoutedEventArgs e) =>
        ScheduleNavigation(ViewModel.Forward);

    private void Toolbar_UpClicked(object sender, RoutedEventArgs e) =>
        ScheduleNavigation(ViewModel.Up);

    private void Toolbar_RefreshClicked(object sender, RoutedEventArgs e)
    {
        Icons.FolderPreviewBinder.ClearCache();
        if (HomeLocation.IsHome(ViewModel.AddressText))
        {
            _homeDashboard?.Reload();
            return;
        }

        ViewModel.Refresh();
    }

    private int _addressSubmissionVersion;

    private async void Omni_PathSubmitted(object? sender, string path)
    {
        var version = ++_addressSubmissionVersion;
        if (SpecialLocation.ShellName(path) is { } shellName)
        {
            try
            {
                // An actual child named "文档" wins over the convenience alias.
                var actualChild = await Task.Run(() =>
                {
                    if (path.Contains(':')) return false;
                    try
                    {
                        var candidate = AddressPath.Normalize(path, ViewModel.AddressText);
                        return Directory.Exists(candidate) || File.Exists(candidate);
                    }
                    catch (ArgumentException) { return false; }
                });
                if (!IsCurrentSubmission()) return;
                if (!actualChild)
                {
                    var folder = await FilesMate.Platform.Windows.Shell.ShellLocation.ResolveFileSystemPathAsync(shellName);
                    if (!IsCurrentSubmission()) return;
                    if (folder is null)
                    {
                        await FilesMate.Platform.Windows.Associations.ClassicExplorer.OpenLocationAsync(shellName);
                        if (IsCurrentSubmission()) Omni.CancelMode();
                    }
                    else if (Omni.TryCommitPath(_ => CommitFolderPath(folder)))
                        ScheduleNavigation(() => ViewModel.Navigate(folder));
                    return;
                }
            }
            catch (Exception error)
            {
                if (IsCurrentSubmission())
                {
                    Omni.TryCommitPath(_ => throw new IOException(Loc.Get("SystemLocation_Failed"), error));
                    ViewModel.ReportUserError(Loc.Get("SystemLocation_Failed"));
                }
                return;
            }
        }
        if (!Omni.TryCommitPath(CommitFolderPath))
        {
            return;
        }

        ScheduleNavigation(() => ViewModel.Navigate(Omni.Text));

        bool IsCurrentSubmission() => !_disposed && version == _addressSubmissionVersion
            && Omni.IsEditing && Omni.DraftText == path;
    }

    private string CommitFolderPath(string draft)
    {
        if (HomeLocation.TryParse(draft, out var home))
        {
            return home;
        }

        var normalized = AddressPath.Normalize(draft, ViewModel.AddressText);
        if (Directory.Exists(normalized) || File.Exists(normalized))
        {
            return normalized;
        }

        throw new DirectoryNotFoundException("This path does not exist.");
    }

    private async void HomeDashboard_PlaceChosen(object? sender, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        ActivateHome(sender);
        if (SpecialLocation.ShellName(path) is { } shellName)
        {
            try { await FilesMate.Platform.Windows.Associations.ClassicExplorer.OpenLocationAsync(shellName); }
            catch (Exception error) { ViewModel.ReportUserError(error.Message); }
            return;
        }
        if (HomeLocation.IsHome(path) || TagLocation.IsTag(path) || Directory.Exists(path))
        {
            ScheduleNavigation(() => ViewModel.Navigate(path));
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            ScheduleNavigation(() => ViewModel.Navigate(parent));
        }
    }

    private void HomeDashboard_PlaceActionRequested(object? sender, PlaceContextInvokedEventArgs e) =>
        Sidebar.InvokePlaceAction(e.Action, e.Item, e.Path);

    private void ShellRoot_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            if (Omni.IsSearchSource(source))
            {
                Omni.DismissCrumbFolders();
                return;
            }

            if (Omni.IsCrumbFolderSource(source))
            {
                Omni.DismissSearch();
                return;
            }
        }

        Omni.DismissSearch();
        Omni.DismissCrumbFolders();
    }

    private void Places_PlaceChosen(object? sender, string path) =>
        ScheduleNavigation(() => ViewModel.Navigate(path));

    private void Sidebar_OpenInNewTabRequested(object? sender, string path) =>
        App.CurrentWindow?.OpenFolderInNewTab(path);

    private void Sidebar_OpenInNewWindowRequested(object? sender, string path) =>
        App.CurrentWindow?.OpenFolderInNewWindow(path);

    private void Sidebar_WhoLocksRequested(object? sender, string path) =>
        _ = _fileActions.ShowWhoLocksAsync([path]);

    private void Omni_SearchChosen(object? sender, string path) =>
        OpenLaunchTarget(LaunchPath.Parse(["/open", path]));

    private void Omni_ModeCanceled(object? sender, EventArgs e)
    {
        // Finish the TextBox/Popup input event before moving focus out of it.
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && !Omni.IsSearchOpen && !Omni.IsEditing)
                ActiveSurface.Focus(FocusState.Programmatic);
        });
    }

    private void Address_CrumbClicked(object? sender, string path) =>
        ScheduleNavigation(() => ViewModel.Navigate(path));

    private void PaneToggle_Click(object sender, RoutedEventArgs e)
    {
        _paneUserToggled = true;
        try
        {
            App.SetSidebarCollapsed(ShellSplit.IsPaneOpen);
            ApplyPaneOpen(null);
        }
        catch (Exception error)
        {
            App.LogFailure("Save navigation visibility", error);
        }
    }

    private void ClosePaneAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ShelfCard.Visibility == Visibility.Visible)
        {
            _shelfPanel?.RequestClose();
            args.Handled = true;
            return;
        }
        if (LockOverlay.Visibility == Visibility.Visible)
        {
            HideLockOverlay();
            args.Handled = true;
            return;
        }

        if (Omni.CancelMode())
        {
            args.Handled = true;
            return;
        }

        if (ShellSplit.DisplayMode == SplitViewDisplayMode.Overlay && ShellSplit.IsPaneOpen)
        {
            ShellSplit.IsPaneOpen = false;
            args.Handled = true;
        }
    }

    private void Sidebar_SettingsClicked(object? sender, EventArgs e) =>
        App.CurrentWindow?.OpenSettings();

    private void Sidebar_PinnedLocationsChanged(object? sender, EventArgs e)
    {
        SyncCommandBar();
        Sidebar.SelectPath(ViewModel.AddressText);
        _homeDashboard?.Reload();
    }

    private void App_ShortcutsChanged(object? sender, EventArgs e) => ApplyShortcuts();

    private void App_TagsChanged(object? sender, EventArgs e)
    {
        ResetTagLoads();
        lock (_tagCacheGate)
        {
            _tagCache.Clear();
            _tagCacheOrder.Clear();
        }

        _hasTagDefinitions = null;
        _tagCatalogProbe = null;
        FileSurface.RefreshRealizedTags();
        _rightSurface?.RefreshRealizedTags();
        _homeDashboard?.Reload();
        _rightHome?.Reload();
    }

    private void ApplyShortcuts()
    {
        ApplyShortcut(AddressEditAccelerator, ShortcutAction.EditAddress);
        ApplyShortcut(SearchAccelerator, ShortcutAction.FilterFolder);
        ApplyShortcut(RefreshAccelerator, ShortcutAction.Refresh);
        ApplyShortcut(CutAccelerator, ShortcutAction.Cut);
        ApplyShortcut(CopyAccelerator, ShortcutAction.Copy);
        ApplyShortcut(CopyPathAccelerator, ShortcutAction.CopyPath);
        ApplyShortcut(PasteAccelerator, ShortcutAction.Paste);
        ApplyShortcut(UndoAccelerator, ShortcutAction.Undo);
        ApplyShortcut(RedoAccelerator, ShortcutAction.Redo);
        ApplyShortcut(NewFolderAccelerator, ShortcutAction.NewFolder);
        ApplyShortcut(RenameAccelerator, ShortcutAction.Rename);
        ApplyShortcut(TerminalAccelerator, ShortcutAction.OpenTerminal);
        ApplyShortcut(RecycleAccelerator, ShortcutAction.Recycle);
        ApplyShortcut(PermanentDeleteAccelerator, ShortcutAction.PermanentDelete);
        ApplyShortcut(PropertiesAccelerator, ShortcutAction.Properties);
        ApplyShortcut(PreviewAccelerator, ShortcutAction.Preview);
        ApplyShortcut(CommandPaletteAccelerator, ShortcutAction.CommandPalette);
    }

    private static void ApplyShortcut(KeyboardAccelerator accelerator, ShortcutAction action)
    {
        var gesture = App.Shortcuts[action];
        accelerator.Key = (VirtualKey)(int)gesture.Key;
        accelerator.Modifiers = (VirtualKeyModifiers)(int)gesture.Modifiers;
    }

    private void ShellSplit_PaneOpened(SplitView sender, object args)
    {
        Sidebar.Reload();
        if (sender.DisplayMode == SplitViewDisplayMode.Overlay)
        {
            Sidebar.Focus(FocusState.Programmatic);
        }
    }

    private void AddressEditAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Omni.BeginPathEdit();
        args.Handled = true;
    }

    private void SearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Omni.BeginSearch();
        args.Handled = true;
    }

    private void FileSurface_OpenRequested(object? sender, FileEntryCore entry)
    {
        ActivateFromSurface(sender as FileDetailsSurface);
        var path = ViewModel.FullPath(entry);
        if (entry.Kind == EntryKind.Directory && App.ExplorerPreferences.OpenFoldersInNewTab)
        {
            App.CurrentWindow?.OpenFolderInNewTab(path);
            return;
        }

        if (entry.Kind == EntryKind.Directory)
        {
            ScheduleNavigation(() => ViewModel.Navigate(path));
            return;
        }

        ViewModel.Open(entry);
    }

    private void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Icons.FolderPreviewBinder.ClearCache();
        if (HomeLocation.IsHome(ViewModel.AddressText))
        {
            ActiveHome.Reload();
        }
        else
        {
            ViewModel.Refresh();
        }

        args.Handled = true;
    }

    private void FileSurface_UpRequested(object? sender, EventArgs e)
    {
        ActivateFromSurface(sender as FileDetailsSurface);
        ScheduleNavigation(ViewModel.Up);
    }

    private void FileSurface_BackRequested(object? sender, EventArgs e)
    {
        ActivateFromSurface(sender as FileDetailsSurface);
        ScheduleNavigation(ViewModel.Back);
    }

    private void FileSurface_ForwardRequested(object? sender, EventArgs e)
    {
        ActivateFromSurface(sender as FileDetailsSurface);
        ScheduleNavigation(ViewModel.Forward);
    }

    private void BackAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ScheduleNavigation(ViewModel.Back);
        args.Handled = true;
    }

    private void ForwardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ScheduleNavigation(ViewModel.Forward);
        args.Handled = true;
    }

    private void UpAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ScheduleNavigation(ViewModel.Up);
        args.Handled = true;
    }

    private void FileSurface_SortRequested(object? sender, EntrySortColumn column)
    {
        ActivateFromSurface(sender as FileDetailsSurface);
        ViewModel.SetSortColumn(column);
    }

    private void FileSurface_CopyPathRequested(object? sender, EventArgs e)
    {
        ActivateFromSurface(sender as FileDetailsSurface);
        CopySelectedPaths();
    }

    private void FileSurface_RefreshRequested(object? sender, EventArgs e)
    {
        Icons.FolderPreviewBinder.ClearCache();
        ActivateFromSurface(sender as FileDetailsSurface);
        if (HomeLocation.IsHome(ViewModel.AddressText))
        {
            ActiveHome.Reload();
            return;
        }

        ViewModel.Refresh();
    }

    private void FileSurface_OpenInNewTabRequested(object? sender, FileEntryCore entry)
    {
        if (entry.Kind != EntryKind.Directory)
        {
            return;
        }

        ActivateFromSurface(sender as FileDetailsSurface);
        App.CurrentWindow?.OpenFolderInNewTab(ViewModel.FullPath(entry));
    }

    private bool _selectionPreviewRequested;
    private void FileSurface_SelectionChanged(object? sender, EventArgs e)
    {
        if (sender is FileDetailsSurface surface)
        {
            var chrome = ReferenceEquals(surface, _rightSurface) ? _rightChrome! : PaneChrome;
            var count = surface.Selection.Count;
            chrome.SelectionText = count > 0 ? StringTable.Format("Status_Selected", count) : string.Empty;
        }
#if FILESMATE_UI_TEST
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "selection-test.log"), $"{DateTime.Now:HH:mm:ss} Selection {e.GetType().Name} preview={_previewVisible}\n");
#endif
        _selectionPreviewRequested = e is not ContextSelectionChangedEventArgs;
        if (!_selectionPreviewRequested)
        {
            _previewHost?.CancelAndClear();
            _loadedPreviewPath = null;
        }
        if (_selectionUiScheduled)
        {
            return;
        }

        _selectionUiScheduled = true;
        if (!DispatcherQueue.TryEnqueue(ApplySelectionUi))
        {
            ApplySelectionUi();
        }
    }

    private void ApplySelectionUi()
    {
#if FILESMATE_UI_TEST
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "selection-test.log"), $"{DateTime.Now:HH:mm:ss} Apply {PrimarySelectedPath()} disposed={_disposed}\n");
#endif
        _selectionUiScheduled = false;
        if (_disposed)
        {
            return;
        }

        UpdateSelectionStatus(FileSurface, PaneChrome);
        if (_rightSurface is not null && _rightChrome is not null)
            UpdateSelectionStatus(_rightSurface, _rightChrome);
        if (ActiveSurface.IsMarqueeSelecting) return;
        SyncCommandBar();
        if (_selectionPreviewRequested) RefreshQuickPreview();
        if (_previewVisible && _selectionPreviewRequested)
        {
            _ = LoadSelectedPreviewAsync();
        }
    }

    private static void UpdateSelectionStatus(FileDetailsSurface surface, FilePaneChrome chrome)
    {
        var count = surface.Selection.Count;
        chrome.SelectionText = count > 0
            ? StringTable.Format("Status_Selected", count)
            : string.Empty;
        // Count is immediate; aggregate sizes and previews settle on release.
        if (surface.IsMarqueeSelecting)
        {
            chrome.SizeText = string.Empty;
            return;
        }
        var bytes = surface.SelectedFileBytes();
        chrome.SizeText = count > 0 && bytes > 0
            ? DriveCapacity.FormatBytes(bytes)
            : string.Empty;
    }

    private void Clipboard_ContentChanged(object? sender, object e) => SyncCommandBar();

    private void SyncCommandBar()
    {
        var clipboard = PaneFileActions.ClipboardHasFiles();
        FileSurface.ClipboardHasFiles = clipboard;
        if (_rightSurface is not null)
        {
            _rightSurface.ClipboardHasFiles = clipboard;
        }

        var home = HomeLocation.IsHome(ViewModel.AddressText);
        Commands.ApplyContext(CommandContext.ForToolbar(
            home ? 0 : ActiveSurface.Selection.Count,
            ActiveSurface.PrimaryIsDirectory(),
            isFolderWritable: !home,
            clipboardHasFiles: clipboard,
            canRefresh: ActiveSurface.CanRefresh,
            shareAvailable: !home && App.ShareService is not null));
    }

    private void Commands_CopyPathClicked(object sender, RoutedEventArgs e) => CopySelectedPaths();

    private void Commands_CommandInvoked(object? sender, AppCommandId id) => RunFileCommand(id);

    private void RunFileCommand(AppCommandId id)
    {
        if (id == AppCommandId.SearchCommands) { _ = ShowCommandPaletteAsync(); return; }
        if (id == AppCommandId.SelectSameType) { ActiveSurface.SelectSameType(); return; }
        if (id == AppCommandId.InvertSelection) { ActiveSurface.InvertSelection(); return; }
        if (id is AppCommandId.CopyToOtherPane or AppCommandId.MoveToOtherPane) { _ = TransferToOtherPaneAsync(id == AppCommandId.MoveToOtherPane); return; }
        if (id == AppCommandId.Rename && ActiveSurface.Selection.Count == 1) { ActiveSurface.BeginInlineRename(); return; }
        if (id == AppCommandId.AddToFavorites)
        {
            _ = AddSelectedFavoritesAsync();
            return;
        }
        if (id is AppCommandId.AddToShelf or AppCommandId.ShowShelf
            or AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover or AppCommandId.ResetFolderView)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed) _ = RunCustomizationCommandAsync(id);
            });
            return;
        }
        if (id == AppCommandId.AddTags)
        {
            ShowTagPicker(ActiveSurface);
            return;
        }

        if (id == AppCommandId.ManageTags)
        {
            App.CurrentWindow?.OpenSettings("tags");
            return;
        }

        if (id == AppCommandId.Share)
        {
            _ = ShareSelectedAsync();
            return;
        }

        if (id == AppCommandId.OpenInNewWindow)
        {
            var path = PrimarySelectedPath();
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                App.CurrentWindow?.OpenFolderInNewWindow(path);
            }

            return;
        }

        if (id == AppCommandId.CopyPathQuoted)
        {
            CopySelectedPaths(quoted: true);
            return;
        }

        if (id is AppCommandId.PinToSidebar or AppCommandId.UnpinFromSidebar)
        {
            var path = PrimarySelectedPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (id == AppCommandId.PinToSidebar)
            {
                WindowsNavigationSource.Pin(path, _pinnedLocations);
            }
            else
            {
                WindowsNavigationSource.Unpin(path, _pinnedLocations);
            }

            App.NotifyPinnedLocationsChanged();
            Sidebar.SelectPath(path);
            SyncCommandBar();
            return;
        }

        _ = _fileActions.RunAsync(id);
    }

    private async Task AddSelectedFavoritesAsync()
    {
        try { await Favorites.AddPathsAsync(SelectedPaths()); }
        catch (Exception error) { ViewModel.ReportUserError(error.Message); }
    }

    private void Favorites_OpenRequested(object? sender, FavoriteEntry entry)
    {
        if (entry.Path is not { } path) return;
        if (entry.IsDirectory) ScheduleNavigation(() => ViewModel.Navigate(path));
        else ViewModel_OpenFileRequested(this, path);
    }

    private async Task ShareSelectedAsync()
    {
        var service = App.ShareService;
        var paths = SelectedPaths();
        if (paths.Count == 0)
        {
            var current = ViewModel.AddressText;
            if (!string.IsNullOrWhiteSpace(current)
                && !HomeLocation.IsHome(current)
                && !TagLocation.IsTag(current)
                && (Directory.Exists(current) || File.Exists(current)))
            {
                paths = [current];
            }
        }

        if (service is null || paths.Count == 0)
        {
            return;
        }

        try
        {
            var result = await service.ShareAsync(paths);
            if (!result.InvokedWindowsShare && result.UsedPathFallback)
            {
                ViewModel.ReportUserError(
                    StringTable.Get("Error_ShareUnavailable"));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            ViewModel.ReportUserError(error.Message);
        }
    }

    public async Task VacateFoldersAsync(IReadOnlyList<string> paths)
    {
        _previewHost?.CancelAndClear();
        FileSurface.CancelFolderSizeWalks();
        _rightSurface?.CancelFolderSizeWalks();
        await VacatePaneAsync(_leftVm, paths).ConfigureAwait(true);
        if (_rightVm is not null)
        {
            await VacatePaneAsync(_rightVm, paths).ConfigureAwait(true);
        }
    }

    public async Task ShowLockOverlayAsync(FrameworkElement content)
    {
        HideLockOverlay();
        var theme = ContentDialogTheme.Resolve(this);
        LockOverlay.RequestedTheme = theme;
        content.RequestedTheme = theme;
        LockOverlayTitle.Text = StringTable.Get("Command_WhoLocks");
        LockOverlayClose.Content = StringTable.Get("Close");
        LockOverlayHost.Content = content;
        LockOverlay.Visibility = Visibility.Visible;
        _lockOverlayClosed = new TaskCompletionSource<bool>();
        await _lockOverlayClosed.Task.ConfigureAwait(true);
    }

    public void HideLockOverlay()
    {
        LockOverlay.Visibility = Visibility.Collapsed;
        LockOverlayHost.Content = null;
        _lockOverlayClosed?.TrySetResult(true);
        _lockOverlayClosed = null;
    }

    private void LockOverlayClose_Click(object sender, RoutedEventArgs e) => HideLockOverlay();

    private void LockOverlayScrim_Tapped(object sender, TappedRoutedEventArgs e) => HideLockOverlay();

    private static async Task VacatePaneAsync(PaneViewModel vm, IReadOnlyList<string> paths)
    {
        var current = vm.Navigation.CurrentPath ?? vm.AddressText;
        if (string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        string? target = null;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            if (FileLockPath.Matches(current, path, directory: true))
            {
                target = path;
                break;
            }
        }

        if (target is null)
        {
            return;
        }

        var parent = Path.GetDirectoryName(target.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(parent) || FileLockPath.Matches(parent, target, directory: true))
        {
            return;
        }

        vm.Navigate(parent);
        await vm.WhenFolderReleased.ConfigureAwait(true);
    }

    private void RefreshFilePanes()
    {
        _leftVm.Refresh();
        _rightVm?.Refresh();
    }

    private Task HandleFileDropAsync(FileDropRequest request, PaneViewModel vm)
    {
        var destination = request.TargetDirectory;
        if (string.IsNullOrWhiteSpace(destination))
        {
            destination = vm.AddressText;
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            return Task.CompletedTask;
        }

        return request.FromShelf
            ? TransferShelfDropAsync(request, destination, vm)
            : _fileActions.DropAsync(request.Paths, destination, request.Operation, request.AllowSameDirectoryCopy);
    }

    private IReadOnlyList<TagDefinition> ResolveTags(PaneViewModel vm, FileEntryCore entry)
    {
        var store = App.MetadataStore;
        var provider = App.FileIdentityProvider;
        if (store is null || provider is null)
        {
            return [];
        }

        if (_hasTagDefinitions is false)
        {
            return [];
        }

        if (_hasTagDefinitions is null)
        {
            EnsureTagCatalogProbe();
            return [];
        }

        var path = vm.FullPath(entry);
        lock (_tagCacheGate)
        {
            if (_tagCache.TryGetValue(path, out var tags))
            {
                return tags;
            }

            if (_tagLoads.Add(path))
            {
                _ = LoadTagsAsync(
                    path,
                    entry.Id,
                    vm.Navigation.CurrentGeneration,
                    _tagLoadCts.Token);
            }
        }

        return [];
    }

    private void EnsureTagCatalogProbe()
    {
        if (_tagCatalogProbe is not null)
        {
            return;
        }

        var cancellationToken = _tagLoadCts.Token;
        _tagCatalogProbe = Task.Run(
            () => ProbeTagCatalogAsync(cancellationToken),
            cancellationToken);
    }

    private async Task ProbeTagCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            var store = App.MetadataStore;
            if (store is null)
            {
                return;
            }

            var tags = await store.ListTagsAsync(cancellationToken).ConfigureAwait(false);
            await EnqueueUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _hasTagDefinitions = tags.Count > 0;
                _tagNames.Clear();
                foreach (var tag in tags)
                {
                    _tagNames[tag.Id] = tag.Name;
                }

                if (TagLocation.IsTag(ViewModel.AddressText))
                {
                    ApplyTabCaption(ViewModel.AddressText);
                    Omni.Text = ViewModel.AddressText;
                }

                if (_hasTagDefinitions is true)
                {
                    FileSurface.RefreshRealizedTags();
                    _rightSurface?.RefreshRealizedTags();
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Tag decoration must never block core file browsing.
            _hasTagDefinitions = false;
        }
    }

    private async Task LoadTagsAsync(
        string path,
        int entryId,
        long generation,
        CancellationToken cancellationToken)
    {
        var enteredGate = false;
        try
        {
            var store = App.MetadataStore;
            var provider = App.FileIdentityProvider;
            if (store is null || provider is null)
            {
                return;
            }

            await _tagLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            enteredGate = true;
            var identity = await Task.Run(() => provider.Resolve(path), cancellationToken).ConfigureAwait(false);
            var tags = await store.GetTagsAsync(identity, cancellationToken).ConfigureAwait(false);
            await EnqueueUiAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested
                    || generation != ViewModel.Navigation.CurrentGeneration)
                {
                    return;
                }

                lock (_tagCacheGate)
                {
                    _tagCache[path] = tags;
                    _tagCacheOrder.Enqueue(path);
                    while (_tagCache.Count > TagCacheCapacity && _tagCacheOrder.TryDequeue(out var oldest))
                    {
                        _tagCache.Remove(oldest);
                    }
                }

                FileSurface.RefreshRealizedTags(entryId);
                _rightSurface?.RefreshRealizedTags(entryId);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Metadata is an enhancement; a locked/corrupt store must not block browsing.
        }
        finally
        {
            if (enteredGate)
            {
                _tagLoadGate.Release();
            }

            lock (_tagCacheGate)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _tagLoads.Remove(path);
                }
            }
        }
    }

    private UIElement? CreateTagPickerPanel()
    {
        var picker = TryCreateTagPicker();
        return picker is null ? null : TagPickerFlyout.CreatePanel(picker.Value.Store, picker.Value.Identities, OnTagsApplied);
    }

    private void ShowTagPicker(FrameworkElement owner)
    {
        var picker = TryCreateTagPicker();
        if (picker is null)
        {
            return;
        }

        TagPickerFlyout.Show(owner, picker.Value.Store, picker.Value.Identities, OnTagsApplied);
    }

    private (IFileMetadataStore Store, IReadOnlyList<FileIdentity> Identities)? TryCreateTagPicker()
    {
        var store = App.MetadataStore;
        var provider = App.FileIdentityProvider;
        if (store is null || provider is null)
        {
            return null;
        }

        var identities = SelectedPaths()
            .Select(provider.Resolve)
            .ToArray();
        return identities.Length == 0 ? null : (store, identities);
    }

    private void OnTagsApplied()
    {
        foreach (var path in SelectedPaths())
        {
            lock (_tagCacheGate)
            {
                _tagCache.Remove(path);
            }
        }

        _hasTagDefinitions = true;
        FileSurface.RefreshRealizedTags();
        _rightSurface?.RefreshRealizedTags();
    }

    private void ResetTagLoads()
    {
        var previous = _tagLoadCts;
        _tagLoadCts = new CancellationTokenSource();
        previous.Cancel();
        lock (_tagCacheGate)
        {
            _tagLoads.Clear();
        }
    }

    private Task EnqueueUiAsync(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }))
        {
            completion.SetCanceled();
        }

        return completion.Task;
    }

    private void CutAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.Cut);
        args.Handled = true;
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.Copy);
        args.Handled = true;
    }

    private void CopyPathAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        CopySelectedPaths();
        args.Handled = true;
    }

    private void PasteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.Paste);
        args.Handled = true;
    }

    private void UndoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _fileActions.Undo();
        args.Handled = true;
    }

    private void RedoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _fileActions.Redo();
        args.Handled = true;
    }

    private void NewFolderAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.NewFolder);
        args.Handled = true;
    }

    private void RenameAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        RunFileCommand(
            ActiveSurface.Selection.Count > 1
                ? AppCommandId.BatchRename
                : AppCommandId.Rename);
        args.Handled = true;
    }

    private void TerminalAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked()) return;
        OpenTerminalAt(ViewModel.AddressText);
        args.Handled = true;
    }

    private void OpenTerminalAt(string path)
    {
        try { FilesMate.Platform.Windows.Shell.TerminalLaunch.Open(path); }
        catch (Exception error) { ViewModel.ReportUserError(error.Message); }
    }

    private void RecycleAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.Recycle);
        args.Handled = true;
    }

    private void PermanentDeleteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.PermanentDelete);
        args.Handled = true;
    }

    private void PropertiesAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FileAcceleratorsBlocked())
        {
            return;
        }

        _ = _fileActions.RunAsync(AppCommandId.Properties);
        args.Handled = true;
    }

    private void PreviewAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SetPreviewVisible(!_previewVisible);
        args.Handled = true;
    }

    private bool FileAcceleratorsBlocked() =>
        Omni.IsEditing || FocusManager.GetFocusedElement() is TextBox || _shelfPanel?.ContainsFocus() == true;

    private void Commands_SortRequested(object? sender, EntrySortColumn column) =>
        ViewModel.SetSortColumn(column);

    private void Commands_LayoutChanged(object? sender, FileLayoutKind kind)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            ActiveSurface.SetLayout(kind);
            ActiveSurface.Focus(FocusState.Programmatic);
        });
    }

    private void Commands_FolderSizesClicked(object sender, RoutedEventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(ToggleFolderSizes))
        {
            ToggleFolderSizes();
        }
    }

    private void ToggleFolderSizes()
    {
        var next = !App.ExplorerPreferences.ShowFolderSizes;
        Commands.SetFolderSizesActive(next);
        _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowFolderSizes = next });
    }

    private void Commands_DualPaneClicked(object sender, RoutedEventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(ToggleDualPane))
        {
            ToggleDualPane();
        }
    }

    private void DualPaneAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ToggleDualPane();
        args.Handled = true;
    }

    private void Commands_PreviewClicked(object sender, RoutedEventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(() => SetPreviewVisible(!_previewVisible)))
        {
            SetPreviewVisible(!_previewVisible);
        }
    }

    private void SetPreviewVisible(bool visible)
    {
        if (visible) CloseQuickPreview();
        _previewVisible = visible;
        Commands.SetPreviewActive(visible);
        if (visible) PreviewHost.SetVisible(true);
        else _previewHost?.SetVisible(false);
        PreviewResizeThumb.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitterColumn.Width = visible ? new GridLength(6) : new GridLength(0);
        PreviewColumn.Width = visible
            ? new GridLength(ExplorerPreferences.ClampPreviewWidth(App.ExplorerPreferences.PreviewWidth))
            : new GridLength(0);
        PreviewContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) ApplyPreviewWidth(App.ExplorerPreferences.PreviewWidth, save: false);
        if (!visible)
        {
            _loadedPreviewPath = null;
        }
        else
        {
            _ = LoadSelectedPreviewAsync();
        }
    }

    private async Task LoadSelectedPreviewAsync()
    {
        var path = PrimarySelectedPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            if (_loadedPreviewPath is not null)
            {
                _loadedPreviewPath = null;
                _previewHost?.CancelAndClear();
            }

            return;
        }

        if (string.Equals(path, _loadedPreviewPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _loadedPreviewPath = path;
        await PreviewHost.LoadAsync(path, ViewModel.Navigation.CurrentGeneration);
    }

    private IReadOnlyList<string> SelectedPaths() => ActiveSurface.SelectedPaths();

    private string? PrimarySelectedPath() => ActiveSurface.PrimaryPath();

    private void CopySelectedPaths(bool quoted = false)
    {
        var lines = SelectedPaths();
        if (lines.Count == 0)
        {
            var folder = ViewModel.AddressText;
            if (string.IsNullOrWhiteSpace(folder) || HomeLocation.IsHome(folder))
            {
                return;
            }

            lines = [folder];
        }

        var text = quoted
            ? string.Join(Environment.NewLine, lines.Select(path => "\"" + path + "\""))
            : string.Join(Environment.NewLine, lines);
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    private void ViewModel_OpenFileRequested(object? sender, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.ReportUserError(ex.Message);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PaneViewModel vm)
        {
            return;
        }

        var surface = SurfaceOf(vm);
        var isActive = ReferenceEquals(vm, ViewModel);
        if (e.PropertyName == nameof(PaneViewModel.AddressText))
        {
            if (_pendingCreatedItemRename && ReferenceEquals(vm, _pendingSelectPane)
                && !string.Equals(FolderCustomizationStore.Key(Path.GetDirectoryName(_pendingSelectPath)),
                    FolderCustomizationStore.Key(vm.AddressText), StringComparison.OrdinalIgnoreCase))
            {
                _pendingSelectPath = null;
                _pendingCreatedItemRename = false;
            }
            if (isActive) UpdateShellWindow();
            _ = RestoreFolderViewAsync(vm);
        }
        if (e.PropertyName == nameof(PaneViewModel.Sort))
            PersistFolderView(vm);
        if (e.PropertyName is null
            or nameof(PaneViewModel.CanGoBack)
            or nameof(PaneViewModel.CanGoForward)
            or nameof(PaneViewModel.CanGoUp)
            or nameof(PaneViewModel.CanRefresh)
            or nameof(PaneViewModel.AddressText))
        {
            if (isActive)
            {
                ScheduleChrome();
            }
        }

        // ViewIndex and Store are published as one logical snapshot. PaneViewModel
        // raises both notifications for non-UI consumers, so binding on each one
        // would reset ItemsRepeater twice inside the same dispatcher turn. WinUI
        // can surface that re-entrant reset as STATUS_STOWED_EXCEPTION when the
        // user navigates while the startup directory is still materializing.
        if (e.PropertyName is null
            or nameof(PaneViewModel.Store)
            or nameof(PaneViewModel.Sort))
        {
            if (!HomeLocation.IsHome(vm.AddressText))
            {
                surface.Bind(vm.Store, vm.ViewIndex, vm.Navigation.CurrentGeneration);
                surface.SetSort(vm.Sort);
                TryApplyPendingSelection(vm);
            }
        }

        if (e.PropertyName is nameof(PaneViewModel.IsLoading)
            && !vm.IsLoading
            && isActive)
        {
            TryApplyPendingSelection(vm);
        }

        if (e.PropertyName is null
            or nameof(PaneViewModel.ItemCount)
            or nameof(PaneViewModel.IsLoading)
            or nameof(PaneViewModel.ErrorText)
            or nameof(PaneViewModel.FilterQuery)
            or nameof(PaneViewModel.StatusText))
        {
            if (HomeLocation.IsHome(vm.AddressText))
            {
                ApplyHomeSurface(vm);
            }
            else
            {
                UpdateEmptyHint(vm);
            }
        }
    }

    public void OpenLaunchTarget(LaunchTarget target)
    {
        _pendingCreatedItemRename = false;
        _pendingSelectPath = target.SelectPath;
        _pendingSelectPane = ViewModel;
        _selectAttempts = 0;
        if (string.IsNullOrEmpty(target.Folder))
        {
            // A search hit that vanished since it was indexed: say so in the status line rather than doing nothing.
            if (target.MissingPath is { } missing) ViewModel.ReportUserError(StringTable.Get("NotFound_Title") + " · " + missing);
            return;
        }

        ScheduleNavigation(() => ViewModel.Navigate(target.Folder!));
    }

    private void TryApplyPendingSelection(PaneViewModel vm)
    {
        if (_disposed || !ReferenceEquals(vm, _pendingSelectPane)
            || string.IsNullOrEmpty(_pendingSelectPath) || vm.IsLoading || vm.Store is null
            || _restoringViews.ContainsKey(vm) || _applyingView.Contains(vm)
            || vm.ViewIndex is null || vm.ViewIndex.Sort != vm.Sort
            || vm.ViewIndex.Generation != vm.Navigation.CurrentGeneration)
        {
            return;
        }

        var pending = _pendingSelectPath;
        if (!string.Equals(FolderCustomizationStore.Key(Path.GetDirectoryName(pending)),
                FolderCustomizationStore.Key(vm.Navigation.CurrentPath), StringComparison.OrdinalIgnoreCase))
            return;
        var name = Path.GetFileName(pending);
        if (string.IsNullOrEmpty(name))
        {
            _pendingSelectPath = null;
            return;
        }

        var surface = SurfaceOf(vm);
        if (surface.TrySelectByPath(pending) || surface.TrySelectByName(name))
        {
            var rename = _pendingCreatedItemRename;
            _pendingCreatedItemRename = false;
            _pendingSelectPath = null;
            _selectAttempts = 0;
            if (rename && IsLoaded && ReferenceEquals(vm, ViewModel)) surface.BeginInlineRename();
            return;
        }

        if (_disposed || _selectAttempts >= 20)
        {
            return;
        }

        _selectAttempts++;
        _ = RetryPendingSelectionAsync(vm, pending);
    }

    private async Task RetryPendingSelectionAsync(PaneViewModel vm, string pending)
    {
        await Task.Delay(50).ConfigureAwait(true);
        if (_disposed || _pendingSelectPath != pending)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() => TryApplyPendingSelection(vm));
    }

    private void AfterNavigate()
    {
        if (_disposed)
        {
            return;
        }

        ResetTagLoads();
        _tagCatalogProbe = null;
        Omni.ApplyCommittedPath(ViewModel.AddressText);
        Sidebar.SelectPath(ViewModel.AddressText);
        ApplyTabCaption(ViewModel.AddressText);
        if (RecentFolderStore.NormalizePath(ViewModel.AddressText) is not null)
        {
            if (Application.Current is App app)
            {
                app.RecordRecentFolder(ViewModel.AddressText);
            }
        }

        ApplyHomeSurface(ViewModel);
        if (HomeLocation.IsHome(ViewModel.AddressText))
        {
            ActiveHome.Focus(FocusState.Programmatic);
        }
        else if (string.IsNullOrEmpty(_pendingSelectPath))
        {
            ActiveSurface.Focus(FocusState.Programmatic);
        }

        TryApplyPendingSelection(ViewModel);
    }

    private void ScheduleChrome()
    {
        if (_chromeScheduled)
        {
            return;
        }

        _chromeScheduled = true;
        if (!DispatcherQueue.TryEnqueue(FlushChrome))
        {
            FlushChrome();
        }
    }

    private void FlushChrome()
    {
        _chromeScheduled = false;
        if (_disposed)
        {
            return;
        }

        SyncChrome();
    }

    private void SyncChrome()
    {
        Omni.CanGoBack = ViewModel.CanGoBack;
        Omni.CanGoForward = ViewModel.CanGoForward;
        Omni.CanGoUp = ViewModel.CanGoUp;
        Omni.CanRefresh = ViewModel.CanRefresh;
        ActiveSurface.CanRefresh = ViewModel.CanRefresh;
        SyncCommandBar();
        ActiveChrome.StatusText = ViewModel.StatusText;
        if (!Omni.IsEditing && !string.IsNullOrEmpty(ViewModel.AddressText))
        {
            Omni.Text = ViewModel.AddressText;
            ApplyTabCaption(ViewModel.AddressText);
        }
    }

    private void PaneChrome_RetryRequested(object sender, RoutedEventArgs e)
    {
        ActivateFromChrome(sender as FilePaneChrome);
        ViewModel.Refresh();
    }

    private void PaneChrome_GoUpRequested(object sender, RoutedEventArgs e)
    {
        ActivateFromChrome(sender as FilePaneChrome);
        ScheduleNavigation(ViewModel.Up);
    }

    private void ScheduleNavigation(Action navigate)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (_disposed)
                {
                    return;
                }

                navigate();
                AfterNavigate();
            }))
        {
            if (_disposed)
            {
                return;
            }

            navigate();
            AfterNavigate();
        }
    }

    private void UpdateEmptyHint(PaneViewModel vm)
    {
        var chrome = ChromeOf(vm);
        chrome.ErrorText = vm.ErrorText;
        chrome.IsLoading = vm.IsLoading;
        chrome.ItemCount = vm.ItemCount;
        chrome.FilterQuery = vm.FilterQuery;
        chrome.CanGoUp = vm.CanGoUp;
        chrome.NavigationGeneration = vm.Navigation.CurrentGeneration;
        chrome.StatusText = vm.StatusText;
        UpdateFolderStatus(vm);
        if (ReferenceEquals(vm, ViewModel))
        {
            RefreshLayoutChrome();
        }
    }

    private void RefreshLayoutChrome()
    {
        Commands.SetLayout(ActiveSurface.LayoutKind);
        UpdateFolderStatus(ViewModel);
    }

    private void ApplyHomeSurface(PaneViewModel vm)
    {
        if (HomeLocation.IsHome(vm.AddressText))
        {
            ShowHomeSurface(vm, bindFiles: true);
            return;
        }

        var surface = SurfaceOf(vm);
        var home = ReferenceEquals(vm, _rightVm) ? _rightHome : _homeDashboard;
        if (home is not null)
        {
            home.Visibility = Visibility.Collapsed;
            home.Opacity = 0;
            home.IsHitTestVisible = false;
        }
        surface.Opacity = 1;
        surface.IsHitTestVisible = true;
        SetHomeOverlay(vm, false);
    }

    private void ShowHomeSurface(PaneViewModel vm, bool bindFiles)
    {
        var chrome = ChromeOf(vm);
        var surface = SurfaceOf(vm);
        var home = HomeOf(vm);
        chrome.ItemCount = 1;
        chrome.ErrorText = null;
        chrome.IsLoading = false;
        chrome.StatusText = StringTable.Get("Home");
        UpdateFolderStatus(vm);
        surface.Opacity = 0;
        surface.IsHitTestVisible = false;
        home.Visibility = Visibility.Visible;
        home.Opacity = 1;
        home.IsHitTestVisible = true;
        if (!HomeOverlayOf(vm))
        {
            if (bindFiles)
            {
                surface.Bind(null, null, vm.Navigation.CurrentGeneration);
            }

            SetHomeOverlay(vm, true);
        }
    }

    private FileDetailsSurface ActiveSurface =>
        _rightActive && _rightSurface is not null ? _rightSurface : FileSurface;

    private FilePaneChrome ActiveChrome =>
        _rightActive && _rightChrome is not null ? _rightChrome : PaneChrome;

    private HomeDashboard ActiveHome =>
        _rightActive && _rightHome is not null ? _rightHome : HomeDashboard;

    private FileDetailsSurface SurfaceOf(PaneViewModel vm) =>
        ReferenceEquals(vm, _rightVm) && _rightSurface is not null ? _rightSurface : FileSurface;

    private FilePaneChrome ChromeOf(PaneViewModel vm) =>
        ReferenceEquals(vm, _rightVm) && _rightChrome is not null ? _rightChrome : PaneChrome;

    private HomeDashboard HomeOf(PaneViewModel vm) =>
        ReferenceEquals(vm, _rightVm) && _rightHome is not null ? _rightHome : HomeDashboard;

    private bool HomeOverlayOf(PaneViewModel vm) =>
        ReferenceEquals(vm, _rightVm) ? _rightHomeOverlay : _homeOverlay;

    private void SetHomeOverlay(PaneViewModel vm, bool value)
    {
        if (ReferenceEquals(vm, _rightVm))
        {
            _rightHomeOverlay = value;
            return;
        }

        _homeOverlay = value;
    }

    private void ActivateFromSurface(FileDetailsSurface? surface) =>
        ActivateRight(surface is not null && ReferenceEquals(surface, _rightSurface));

    private void ActivateFromChrome(FilePaneChrome? chrome) =>
        ActivateRight(chrome is not null && ReferenceEquals(chrome, _rightChrome));

    private void ActivateHome(object? sender) =>
        ActivateRight(sender is not null && ReferenceEquals(sender, _rightHome));

    private void ActivateRight(bool right)
    {
        if (!_dualPane || _rightVm is null)
        {
            right = false;
        }

        if (_rightActive == right && ReferenceEquals(ViewModel, right ? _rightVm : _leftVm))
        {
            PaneChrome.IsActive = !right || !_dualPane;
            if (_rightChrome is not null)
            {
                _rightChrome.IsActive = right;
            }

            return;
        }

        _rightActive = right;
        ViewModel = right ? _rightVm! : _leftVm;
        UpdateShellWindow();
        PaneChrome.IsActive = !right;
        if (_rightChrome is not null)
        {
            _rightChrome.IsActive = right;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (_disposed)
                {
                    return;
                }

                SyncChrome();
                ApplySelectionUi();
                ApplyHomeSurface(ViewModel);
            }))
        {
            SyncChrome();
            ApplySelectionUi();
            ApplyHomeSurface(ViewModel);
        }
    }

    private void ToggleDualPane() => SetDualPane(!_dualPane, persist: true);

    private void ApplyAlphabetNavigationPolicy(ExplorerPreferences preferences)
    {
        FileSurface.SetAlphabetNavigationPolicy(
            preferences.ShowAlphabetNavigation,
            preferences.AlphabetNavigationMinimumItemCount,
            preferences.ShowAlphabetNavigationInDualPane,
            _dualPane);
        _rightSurface?.SetAlphabetNavigationPolicy(
            preferences.ShowAlphabetNavigation,
            preferences.AlphabetNavigationMinimumItemCount,
            preferences.ShowAlphabetNavigationInDualPane,
            _dualPane);
    }

    private void SetDualPane(bool enabled, bool persist)
    {
        if (enabled == _dualPane && (!enabled || _rightVm is not null))
        {
            if (!enabled)
            {
                WorkspaceSplit.Layout = WorkspaceLayoutKind.Single;
                Commands.SetDualPaneActive(false);
            }

            ApplyAlphabetNavigationPolicy(App.ExplorerPreferences);
            PersistDualPane(persist);
            return;
        }

        if (!enabled)
        {
            ActivateRight(false);
            _dualPane = false;
            if (_rightVm is not null) CancelFolderStatus(_rightVm);
            PaneChrome.IsDualPane = false;
            if (_rightChrome is not null)
            {
                _rightChrome.IsDualPane = false;
            }

            WorkspaceSplit.Layout = WorkspaceLayoutKind.Single;
            Commands.SetDualPaneActive(false);
            ApplyAlphabetNavigationPolicy(App.ExplorerPreferences);
            PersistDualPane(persist);
            return;
        }

        EnsureRightPane();
        _dualPane = true;
        PaneChrome.IsDualPane = true;
        PaneChrome.IsTrailingPane = false;
        _rightChrome!.IsDualPane = true;
        _rightChrome.IsTrailingPane = true;
        WorkspaceSplit.Layout = WorkspaceLayoutKind.Vertical;
        Commands.SetDualPaneActive(true);
        ApplyAlphabetNavigationPolicy(App.ExplorerPreferences);
        if (string.IsNullOrEmpty(_rightVm!.AddressText))
        {
            _rightVm.Navigate(DualPaneStartPath());
        }
        UpdateFolderStatus(_rightVm);

        PersistDualPane(persist);
    }

    private void PersistDualPane(bool persist)
    {
        if (!persist || App.ExplorerPreferences.DualPane == _dualPane)
        {
            return;
        }

        _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { DualPane = _dualPane });
    }

    private string DualPaneStartPath()
    {
        var path = _leftVm.AddressText;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = _startupPath;
        }

        if (!string.IsNullOrWhiteSpace(path)
            && !HomeLocation.IsHome(path)
            && !TagLocation.IsTag(path)
            && Directory.Exists(path))
        {
            return path;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrEmpty(desktop)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : desktop;
    }

    private void EnsureRightPane()
    {
        if (_rightVm is not null)
        {
            return;
        }

        _rightVm = CreatePaneViewModel();
        _rightVm.PropertyChanged += ViewModel_PropertyChanged;
        _rightVm.OpenFileRequested += ViewModel_OpenFileRequested;

        _rightSurface = new FileDetailsSurface();
        _rightSurface.ResolvePath = entry => _rightVm.FullPath(entry);
        _rightSurface.ResolveFolder = () => _rightVm.Navigation.CurrentPath ?? _rightVm.AddressText;
        _rightSurface.ResolveTags = entry => ResolveTags(_rightVm, entry);
        _rightSurface.CreateTagPicker = CreateTagPickerPanel;
        _rightSurface.IsPinnedPath = path => WindowsNavigationSource.IsPinned(path, _pinnedLocations);
        _rightSurface.PresentationChanged += (_, _) =>
        {
            if (_rightActive)
            {
                RefreshLayoutChrome();
                PersistFolderView(_rightVm);
            }
        };
        _rightSurface.TerminalRequested += (_, path) => OpenTerminalAt(path);
        ConfigureConvenienceSurface(_rightSurface, true);
        _rightSurface.CommandRequested += (_, id) =>
        {
            ActivateRight(true);
            RunFileCommand(id);
        };
        _rightSurface.DropRequested = request => HandleFileDropAsync(request, _rightVm);
        _rightSurface.CopyPathRequested += FileSurface_CopyPathRequested;
        _rightSurface.OpenInNewTabRequested += FileSurface_OpenInNewTabRequested;
        _rightSurface.OpenRequested += FileSurface_OpenRequested;
        _rightSurface.RefreshRequested += FileSurface_RefreshRequested;
        _rightSurface.SelectionChanged += FileSurface_SelectionChanged;
        _rightSurface.SortRequested += FileSurface_SortRequested;
        _rightSurface.UpRequested += FileSurface_UpRequested;
        _rightSurface.BackRequested += FileSurface_BackRequested;
        _rightSurface.ForwardRequested += FileSurface_ForwardRequested;
        _rightSurface.SetLayout(FileSurface.LayoutKind);

        _rightHome = new HomeDashboard
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _rightHome.PlaceChosen += HomeDashboard_PlaceChosen;
        _rightHome.PlaceActionRequested += HomeDashboard_PlaceActionRequested;

        var body = new Grid();
        body.Children.Add(_rightSurface);
        body.Children.Add(_rightHome);

        _rightChrome = new FilePaneChrome
        {
            IsDualPane = true,
            IsTrailingPane = true,
            IsActive = false,
            Body = body,
            ShowStatusBar = PaneChrome.ShowStatusBar,
        };
        _rightChrome.GoUpRequested += PaneChrome_GoUpRequested;
        _rightChrome.RetryRequested += PaneChrome_RetryRequested;
        _rightChrome.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler((_, _) => ActivateRight(true)),
            handledEventsToo: true);
        WorkspaceSplit.RightContent = _rightChrome;
    }

    private PaneViewModel CreatePaneViewModel() =>
        new(
            new DispatcherQueueUiDispatcher(UiDispatcherQueue.GetForCurrentThread()),
            new WindowsPathService(),
            new TaggedDirectoryEnumerator(
                new WindowsDirectoryEnumerator(),
                async (id, ct) => App.MetadataStore is null
                    ? []
                    : await App.MetadataStore.ListPathsForTagAsync(id, ct).ConfigureAwait(false)),
            WindowsNameComparer.Instance,
            new WindowsDirectoryWatcher());

    internal static string ResolveInitialPath(string? requested)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            return requested;
        }

        var start = App.ExplorerPreferences.StartupFolder == FolderStartupKind.Home
            ? HomeLocation.Uri
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrEmpty(start)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : start;
    }

    private void ApplyTabCaption(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var place = Sidebar.PlaceFor(path, exact: true);
        var tagName = TagLocation.TryParse(path, out var id) && _tagNames.TryGetValue(id, out var name)
            ? name
            : place?.Label;
        FilesMate.App.App.UpdateFolderTab(
            this,
            LocationCaption.Title(path, tagName),
            LocationCaption.Glyph(path) ?? place?.IconGlyph);
    }
}
