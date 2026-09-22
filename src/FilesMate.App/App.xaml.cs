using FilesMate.App.Animations;
using FilesMate.App.Diagnostics;
using FilesMate.App.Localization;
using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;
using FilesMate.App.Services;
using FilesMate.App.ViewModels;
using FilesMate.App.Views;
using FilesMate.App.Sharing;
using FilesMate.App.Shortcuts;
using FilesMate.App.Theming;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.Metadata;
using FilesMate.Platform.Windows.Shell;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

using Windows.UI.ViewManagement;

using WinRT.Interop;

namespace FilesMate.App;

/// <summary>
/// FilesMate application entry. The logical equivalent of WinMain.
/// </summary>
public partial class App : Application
{
    internal static FolderCustomizationStore FolderCustomizations { get; } = new(Program.SettingsPath(FolderCustomizationStore.DefaultFilePath));
    internal static FileShelfStore FileShelf { get; } = new(Program.SettingsPath(FileShelfStore.DefaultFilePath));
    internal static event EventHandler? FolderCoversChanged;
    internal static void NotifyFolderCoversChanged() => FolderCoversChanged?.Invoke(null, EventArgs.Empty);
    private readonly List<MainWindow> _windows = [];
    private UISettings? _systemThemeWatcher;
    private CancellationTokenSource? _autoIndexCts;
    private readonly WindowSessionStore _windowSession = new(Program.SettingsPath(WindowSessionStore.DefaultFilePath));
    private readonly RecentFolderStore _recentFolders = new(Program.SettingsPath(RecentFolderStore.DefaultFilePath));
    private LaunchTarget? _queuedExternalLaunch;
    private LaunchTarget? _lastExternalLaunch;
    private long _lastExternalLaunchTicks;

    public static MainWindow? CurrentWindow { get; private set; }

    public static IMotionService Motion { get; private set; } = new MotionService(new WindowsAnimationSettings());

    public static AppearanceSettingsViewModel? AppearanceViewModel { get; private set; }

    public static IFileMetadataStore? MetadataStore { get; private set; }

    public static IFileNameSearchIndex? SearchIndex { get; private set; }

    public static SearchIndexSettingsService? SearchIndexSettingsStore { get; private set; }

    public static WindowsFileIdentityProvider? FileIdentityProvider { get; private set; }

    public static PreviewService? PreviewService { get; private set; }

    public static IShareService? ShareService { get; private set; }

    public static ShortcutMap Shortcuts { get; private set; } = new();

    internal static FileUndoStack FileUndo { get; } = new();

    internal static bool IsShortcutCaptureActive { get; set; }

    private static ShortcutSettingsService? ShortcutSettingsStore { get; set; }

    private static ExplorerPreferencesService? ExplorerPreferencesStore { get; set; }

    public static event EventHandler<AppearanceSettings>? AppearanceChanged;

    public static event EventHandler? ShortcutsChanged;

    public static event EventHandler? TagsChanged;

    public static event EventHandler? PinnedLocationsChanged;

    public static ExplorerPreferences ExplorerPreferences { get; private set; } = ExplorerPreferences.Default;

    public static event EventHandler<ExplorerPreferences>? ExplorerPreferencesChanged;
    internal static event EventHandler? FolderHandlerChanged;
    internal static void NotifyFolderHandlerChanged() => FolderHandlerChanged?.Invoke(null, EventArgs.Empty);

    public App()
    {
        StartupClock.Mark("AppConstructor");
        AppUserModel.RegisterCurrentProcess();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        try
        {
            ApplyApplicationTheme(new AppearanceSettingsService(Program.SettingsPath(AppearanceSettingsService.DefaultFilePath)).Load());
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Startup theme failed: {0}", error);
        }

        LanguageSettings.Apply(Program.SettingsPath(LanguageSettings.DefaultFilePath));
        InitializeComponent();
        StringTable.UseUiCulture = true;
        try
        {
            var settingsPath = Program.SettingsPath(ExplorerPreferencesService.DefaultFilePath);
            ReplacementBackupBudget.Shared.Initialize(Path.Combine(Path.GetDirectoryName(settingsPath)!, "undo-backups"));
        }
        catch (Exception error) { LogFailure("UndoBackupJournal", error); }
    }

    internal static void LogFailure(string source, Exception error) =>
        AppendCrashRecord(source, error);

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        AppendCrashRecord("XamlUnhandled", args.Exception);
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs args) =>
        AppendCrashRecord("AppDomainUnhandled", args.ExceptionObject as Exception);

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        AppendCrashRecord("UnobservedTask", args.Exception);
        args.SetObserved();
    }

    internal static void AppendCrashRecord(string source, Exception? error)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FilesMate");
            Directory.CreateDirectory(directory);
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var details = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Process={0} PID={1} PrivateBytes={2} WorkingSet={3} Handles={4} Threads={5} HResult=0x{6:X8}",
                Environment.ProcessPath,
                Environment.ProcessId,
                process.PrivateMemorySize64,
                process.WorkingSet64,
                process.HandleCount,
                process.Threads.Count,
                error?.HResult ?? 0);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{details}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash diagnostics must never replace the original failure.
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var store = new AppearanceSettingsService(Program.SettingsPath(AppearanceSettingsService.DefaultFilePath));
        var settings = store.Load();
        AppearanceViewModel = new AppearanceSettingsViewModel(store, settings, ApplyAppearance);
        StartSystemThemeWatcher();
        MetadataStore = new SqliteFileMetadataStore(SqliteFileMetadataStore.DefaultFilePath);
        SearchIndexSettingsStore = new SearchIndexSettingsService(Program.SettingsPath(SearchIndexSettingsService.DefaultFilePath));
        SearchIndex = new FileNameIndexService(SearchIndexSettingsStore.Load().ResolveDatabasePath());
        ShortcutSettingsStore = new ShortcutSettingsService(ShortcutSettingsService.DefaultFilePath);
        Shortcuts = ShortcutSettingsStore.Load();
        ExplorerPreferencesStore = new ExplorerPreferencesService(Program.SettingsPath(ExplorerPreferencesService.DefaultFilePath));
        ExplorerPreferences = ExplorerPreferencesStore.Load();
        ExplorerPreferenceBridge.ShowHiddenFiles = () => ExplorerPreferences.ShowHiddenFiles;
        ExplorerPreferenceBridge.ShowFolderSizes = () => ExplorerPreferences.ShowFolderSizes;
        ExplorerPreferenceBridge.TryFolderSize = FolderSizeCache.TryGet;
        ExplorerPreferenceBridge.InvalidateFolderSizes = FolderSizeCache.Invalidate;
        FolderSizeCache.SizeCached += OnFolderSizeCached;
        if (!Program.IsUiTestBuild) RepairFolderHandlers();
        if (LaunchPath.IsExplorerHost(Environment.GetCommandLineArgs()))
        {
            LaunchClassicExplorer();
            Exit();
            return;
        }

        if (!Program.IsUiTestBuild) _ = GlobalSearchService.EnsureStartedAsync();
        FileIdentityProvider = new WindowsFileIdentityProvider();
        PreviewService = new PreviewService(
        [
            new ImagePreviewProvider(),
            new TextPreviewProvider(),
            new PdfPreviewProvider(),
            new OfficePreviewProvider(),
            new MediaPreviewProvider(),
            new PropertiesPreviewProvider(),
        ]);
        Motion = new MotionService(new CompositeAnimationSettings(
            new WindowsAnimationSettings(),
            () => AppearanceViewModel.Current.ReduceMotion));

        ApplyApplicationTheme(settings);

        var launch = AppLifecycle.PendingLaunch;
        var restored = Program.LanguageRestart?.Windows;
        var window = new MainWindow(new AppPageFactory(), launch, hostTearOut: false, restored?.FirstOrDefault());
        TrackWindow(window);
        DeliverQueuedLaunch(window);
        foreach (var redirected in Program.IsUiTestBuild ? [] : AppLifecycle.TakePendingRedirects())
        {
            HandleRedirectedLaunch(redirected);
        }
        ShareService = new WindowsShareService(window.NativeHandle);
        ApplyAppearance(settings);
        window.Activate();
        if (restored is { Length: > 1 })
        {
            foreach (var session in restored.Skip(1))
            {
                var additional = new MainWindow(new AppPageFactory(), launch, hostTearOut: false, session);
                TrackWindow(additional);
                additional.ApplyAppearance(settings);
                additional.Activate();
            }
        }
        ScheduleFeatureSetup(window);
        if (!Program.IsUiTestBuild)
        {
            _ = CheckForUpdatesAfterStartupAsync();
            _ = RefreshJumpListAsync();
            _autoIndexCts = new CancellationTokenSource();
            _ = RunAutoIndexAsync(_autoIndexCts.Token);
        }
    }

    internal void HandleRedirectedLaunch(LaunchTarget target)
    {
        if (target.Folder is null && target.SelectPath is null && target.SettingsSection is null && !target.ActivateOnly)
        {
            ToggleMainWindowFromActivation();
            return;
        }

        if (IsDuplicateExternalLaunch(target))
        {
            var window = CurrentWindow ?? (_windows.Count > 0 ? _windows[0] : null);
            if (window is not null)
            {
                window.DispatcherQueue.TryEnqueue(() =>
                {
                    if (window.AppWindow.Presenter is OverlappedPresenter presenter
                        && presenter.State == OverlappedPresenterState.Minimized)
                    {
                        presenter.Restore();
                    }

                    window.Activate();
                    AppLifecycle.TryBringToForeground(window.NativeHandle);
                });
            }

            return;
        }

        if (_windows.Count == 0)
        {
            _queuedExternalLaunch = target;
            return;
        }

        var existing = CurrentWindow ?? _windows[0];
        DeliverExternalLaunch(existing, target);
    }

    internal void ToggleMainWindowFromActivation()
    {
        var window = CurrentWindow ?? (_windows.Count > 0 ? _windows[0] : null);
        if (window is null)
        {
            AppLifecycle.TraceInternal("Toggle skipped: no main window");
            return;
        }

        var queued = window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                window.ToggleTaskbarVisibility();
            }
            catch (Exception error)
            {
                LogFailure("TaskbarToggle", error);
            }
        });

        AppLifecycle.TraceInternal(queued
            ? "Toggle queued"
            : "Toggle skipped: dispatcher queue rejected request");
    }

    private bool IsDuplicateExternalLaunch(LaunchTarget target)
    {
        var now = Environment.TickCount64;
        if (_lastExternalLaunch is { } last
            && now - _lastExternalLaunchTicks < 2000
            && last.SameDestination(target))
        {
            return true;
        }

        _lastExternalLaunch = target;
        _lastExternalLaunchTicks = now;
        return false;
    }

    private void DeliverQueuedLaunch(MainWindow window)
    {
        if (_queuedExternalLaunch is not { } queued)
        {
            return;
        }

        _queuedExternalLaunch = null;
        DeliverExternalLaunch(window, queued);
    }

    private static void DeliverExternalLaunch(MainWindow window, LaunchTarget target)
    {
        window.DispatcherQueue.TryEnqueue(() =>
        {
            window.HandleLaunch(target);
            if (window.AppWindow.Presenter is OverlappedPresenter presenter
                && presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            window.Activate();
            AppLifecycle.TryBringToForeground(window.NativeHandle);
        });
    }

    internal void RecordRecentFolder(string? path)
    {
        var updated = _recentFolders.Record(path);
        _ = RefreshJumpListAsync(updated);
    }

    private Task RefreshJumpListAsync() => RefreshJumpListAsync(_recentFolders.Load());

    private static async Task RefreshJumpListAsync(IReadOnlyList<string> folders)
    {
        if (Program.IsUiTestBuild) return;
        try
        {
            await JumpListService.ApplyRecentFoldersAsync(folders).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Jump list refresh failed: {0}", ex);
        }
    }

    internal void SaveWindowSession(MainWindow window)
    {
        if (!ExplorerPreferences.RestoreLastSession || window.IsHostTearOut)
        {
            return;
        }

        var snapshot = window.CaptureSession();
        if (snapshot.Tabs.Count == 0)
        {
            return;
        }

        try
        {
            _windowSession.Save(snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Session save failed: {0}", ex);
        }
    }

    private static void OnFolderSizeCached() => ExplorerPreferenceBridge.NotifyFolderSizesChanged();

    private static void LaunchClassicExplorer()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                ClassicExplorer.Launch();
                return;
            }

            ClassicExplorer.Launch(new DefaultFolderAssociation(new CurrentUserRegistry()), exe);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Classic Explorer launch failed: {0}", error);
        }
    }

    private static void RepairFolderHandlers()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }

            var association = new DefaultFolderAssociation(new CurrentUserRegistry());
            if (association.HasOurCommand(exe))
            {
                association.Enable(exe);
            }
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Folder handler repair failed: {0}", error);
        }
    }

    private static void ApplyApplicationTheme(AppearanceSettings settings)
    {
        try
        {
            Application.Current.RequestedTheme = settings.Theme switch
            {
                AppThemeKind.Light => ApplicationTheme.Light,
                AppThemeKind.Dark => ApplicationTheme.Dark,
                _ => SystemThemeResolver.ResolveApplicationTheme(),
            };
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Application theme failed: {0}", error);
        }
    }

    private void StartSystemThemeWatcher()
    {
        try
        {
            _systemThemeWatcher = new UISettings();
            _systemThemeWatcher.ColorValuesChanged += SystemTheme_ColorValuesChanged;
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("System theme watcher failed: {0}", error);
        }
    }

    private void SystemTheme_ColorValuesChanged(UISettings sender, object args)
    {
        var window = CurrentWindow;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() =>
        {
            if (AppearanceViewModel?.Current is { Theme: AppThemeKind.System } settings)
            {
                ApplyAppearance(settings);
            }
        });
    }

    private static void ApplyAppearance(AppearanceSettings settings)
    {
        ApplyApplicationTheme(settings);
        Icons.ShellIconBinder.SetUseBundledIcons(settings.UseBundledFileIcons);

        if (Current is App app)
        {
            foreach (var window in app._windows)
            {
                window.ApplyAppearance(settings);
            }
        }

        AppearanceChanged?.Invoke(null, settings);
    }

    internal static void TrackWindow(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Current is not App app || app._windows.Contains(window))
        {
            return;
        }

        app._windows.Add(window);
        CurrentWindow = window;
        window.Closed += app.Window_Closed;
    }

    internal static void NotifyActivated(MainWindow window) => CurrentWindow = window;

    internal static bool IsMemoryReclamationBusy => Current is App app
        && app._windows.Any(window => window.IsMemoryReclamationBusy);

    internal static async Task VacateFoldersAsync(IReadOnlyList<string> paths)
    {
        if (Current is not App app)
        {
            return;
        }

        foreach (var window in app._windows.ToArray())
        {
            await window.VacateFoldersAsync(paths).ConfigureAwait(true);
        }
    }

    internal static MainWindow? WindowForElement(UIElement element)
    {
        var root = element.XamlRoot;
        if (root is null || Current is not App app)
        {
            return null;
        }

        foreach (var window in app._windows)
        {
            if (window.Content?.XamlRoot == root)
            {
                return window;
            }
        }

        return null;
    }

    internal static void UpdateFolderTab(object page, string name, string? glyph = null)
    {
        if (Current is not App app)
        {
            return;
        }

        foreach (var window in app._windows)
        {
            window.SetFolderTab(page, name, glyph);
        }
    }

    internal static async Task<ShortcutAction?> UpdateShortcutAsync(
        ShortcutAction action,
        ShortcutGesture gesture)
    {
        var previous = Shortcuts[action];
        if (!Shortcuts.TrySet(action, gesture, out var conflict))
        {
            return conflict;
        }

        try
        {
            if (ShortcutSettingsStore is not null)
            {
                await ShortcutSettingsStore.SaveAsync(Shortcuts).ConfigureAwait(true);
            }

            ShortcutsChanged?.Invoke(null, EventArgs.Empty);
            return null;
        }
        catch
        {
            _ = Shortcuts.TrySet(action, previous, out _);
            throw;
        }
    }

    internal static async Task ResetShortcutsAsync()
    {
        var previous = Shortcuts;
        var reset = new ShortcutMap();
        try
        {
            if (ShortcutSettingsStore is not null)
            {
                await ShortcutSettingsStore.SaveAsync(reset).ConfigureAwait(true);
            }

            Shortcuts = reset;
            ShortcutsChanged?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            Shortcuts = previous;
            throw;
        }
    }

    internal static void NotifyTagsChanged() => TagsChanged?.Invoke(null, EventArgs.Empty);

    internal static void NotifyPinnedLocationsChanged() =>
        PinnedLocationsChanged?.Invoke(null, EventArgs.Empty);

    internal static async Task SetExplorerPreferencesAsync(ExplorerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var previous = ExplorerPreferences;
        ExplorerPreferences = preferences;
        try
        {
            if (ExplorerPreferencesStore is not null)
            {
                await ExplorerPreferencesStore.SaveAsync(preferences).ConfigureAwait(true);
            }

            ExplorerPreferencesChanged?.Invoke(null, preferences);
        }
        catch
        {
            ExplorerPreferences = previous;
            ExplorerPreferencesChanged?.Invoke(null, previous);
            throw;
        }
    }

    private void RebindShareService()
    {
        if (ShareService is IDisposable share)
        {
            share.Dispose();
        }

        ShareService = new WindowsShareService(_windows[0].NativeHandle);
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (sender is MainWindow closed)
        {
            closed.Closed -= Window_Closed;
            _windows.Remove(closed);
            if (ReferenceEquals(CurrentWindow, closed))
            {
                CurrentWindow = _windows.Count > 0 ? _windows[^1] : null;
            }
        }

        if (_windows.Count > 0)
        {
            RebindShareService();
            if (sender is MainWindow closing)
            {
                SaveWindowSession(closing);
            }

            return;
        }

        // The last window can close within the view store's debounce interval.
        FileUndo.Clear();
        // Complete its atomic write before WinUI ends the process.
        try { FolderCustomizations.FlushAsync().GetAwaiter().GetResult(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppendCrashRecord("SaveFolderViews", error);
        }

        _autoIndexCts?.Cancel();
        _autoIndexCts?.Dispose();
        _autoIndexCts = null;
        Motion.CancelWindowScope();
        CurrentWindow = null;
        AppearanceViewModel = null;
        FileIdentityProvider = null;
        if (ShareService is IDisposable share)
        {
            share.Dispose();
        }

        ShareService = null;
        ShortcutSettingsStore = null;
        ExplorerPreferencesStore = null;
        ExplorerPreferences = ExplorerPreferences.Default;
        ExplorerPreferenceBridge.ShowHiddenFiles = static () => false;
        ExplorerPreferenceBridge.ShowFolderSizes = static () => false;
        ExplorerPreferenceBridge.TryFolderSize = static (string _, out ulong size) =>
        {
            size = 0;
            return false;
        };
        FolderSizeCache.SizeCached -= OnFolderSizeCached;
        IsShortcutCaptureActive = false;
        if (PreviewService is not null)
        {
            _ = PreviewService.DisposeAsync();
            PreviewService = null;
        }
        if (SearchIndex is not null)
        {
            _ = SearchIndex.DisposeAsync();
            SearchIndex = null;
        }

        SearchIndexSettingsStore = null;
        if (MetadataStore is not null)
        {
            _ = MetadataStore.DisposeAsync();
            MetadataStore = null;
        }
    }

    private static async Task RunAutoIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchIndexFreshness.StartupDelay, cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                await TryAutoIndexAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(SearchIndexFreshness.RecheckEvery, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task TryAutoIndexAsync(CancellationToken cancellationToken)
    {
        if (!Features.Completed) return;
        var store = SearchIndexSettingsStore;
        var index = SearchIndex;
        if (store is null || index is null)
        {
            return;
        }

        var settings = store.Load();
        if (!SearchIndexFreshness.ShouldRefresh(index.Stats, DateTimeOffset.UtcNow, settings.AutoRefresh, index.IsRunning)
            && !(settings.AutoRefresh && !index.IsRunning && index is FileNameIndexService { NeedsUpgrade: true }))
        {
            return;
        }

        try
        {
            await index.RebuildAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static readonly SemaphoreSlim IndexRelocationGate = new(1, 1);

    internal static async Task<SearchIndexMigrationResult> RelocateSearchIndexAsync(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        using var operation = FileOperationLifetime.Begin();
        await IndexRelocationGate.WaitAsync();
        try
        {
            var store = SearchIndexSettingsStore ?? throw new InvalidOperationException("Search settings are unavailable.");
            var newPath = Path.Combine(Path.GetFullPath(directory), SearchIndexSettings.DatabaseFileName);
            var previous = SearchIndex;
            var oldPath = previous?.FilePath ?? store.Load().ResolveDatabasePath();
            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return new SearchIndexMigrationResult(newPath, false, null);
            if (previous?.IsRunning == true) throw new IOException("Wait for indexing to finish before moving the index.");
            // Stop this process's readers/writer before the snapshot; host readers open the
            // published path per query and can finish against the old database independently.
            SearchIndex = null;
            if (previous is not null) await previous.DisposeAsync();
            var settingsCommitted = false;
            try
            {
                var result = await Task.Run(() => SearchIndexStorage.MigrateAsync(oldPath, newPath,
                    async (path, token) =>
                    {
                        await store.UpdateDatabaseDirectoryAsync(Path.GetDirectoryName(path)!, token).ConfigureAwait(false);
                        settingsCommitted = true;
                    }));
                SearchIndex = await Task.Run(() => new FileNameIndexService(newPath));
                return result;
            }
            catch
            {
                // A failed settings read defaults its path. Recovery must use the known
                // transaction state, including when malformed settings caused the failure.
                var activePath = settingsCommitted ? newPath : oldPath;
                SearchIndex = await Task.Run(() => new FileNameIndexService(activePath));
                throw;
            }
        }
        finally { IndexRelocationGate.Release(); }
    }
}
