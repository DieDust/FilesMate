using Loc = FilesMate.App.Localization.StringTable;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FilesMate.Platform.Windows.Processes;
using FilesMate.Search;
using Forms = System.Windows.Forms;

namespace FilesMate.SearchHost;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 5 && args[0] == "--native-office-preview")
        {
            FilesMate.App.Localization.LanguageSettings.Apply(FilesMate.App.Localization.LanguageSettings.DefaultFilePath);
            Environment.ExitCode = NativeOfficePreviewWorker.Run(args[1], (nint)long.Parse(args[2]), int.Parse(args[3]), args[4]);
            return;
        }
        if (args.Length == 3 && args[0] == "--office-preview")
        {
            FilesMate.App.Localization.LanguageSettings.Apply(FilesMate.App.Localization.LanguageSettings.DefaultFilePath);
            Environment.ExitCode = OfficePreviewWorker.Run(args[1], args[2]);
            return;
        }
        string? Value(string key) { var index = Array.IndexOf(args, key); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        var profile = Value("--profile") ?? GlobalSearchConfiguration.DefaultDirectory;
        FilesMate.App.Localization.LanguageSettings.Apply(Path.Combine(profile, "language.json"));
        var manager = Value("--manager") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "FilesMate.App.exe"));
        if (Array.IndexOf(args, "--sync-startup") >= 0 || Array.IndexOf(args, "--remove-startup") >= 0)
        {
            try
            {
                if (Array.IndexOf(args, "--remove-startup") >= 0) GlobalSearchStartup.RemoveOwned(Environment.ProcessPath!, manager, profile);
                else if (FeatureSetup.Load(profile).Completed) GlobalSearchStartup.Synchronize(GlobalSearchConfiguration.Load(profile), Environment.ProcessPath!, manager, profile);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { System.Diagnostics.Trace.TraceError(error.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (Array.IndexOf(args, "--startup") >= 0 && (!FeatureSetup.Load(profile).Completed || !GlobalSearchConfiguration.Load(profile).StartAtLogin)) return;
        if (int.TryParse(Value("--after-exit"), out var previousPid))
        {
            try { using var previous = Process.GetProcessById(previousPid); if (!previous.WaitForExit(8000)) return; }
            catch (ArgumentException) { }
        }
        if (Array.IndexOf(args, "--stop") >= 0)
        {
            var stopped = SearchHostClient.SendAsync(new("stop-installation", ManagerPath: manager), profile).GetAwaiter().GetResult();
            if (stopped is { Ok: true, Pid: > 0 })
            {
                try { using var process = Process.GetProcessById(stopped.Pid); process.WaitForExit(5000); }
                catch (ArgumentException) { }
            }
            return;
        }
        if (Array.IndexOf(args, "--background") >= 0 && Array.IndexOf(args, "--configure") < 0 && !FeatureSetup.Load(profile).Completed) return;
        if (DetachedProcess.IsInKillOnCloseJob())
        {
            // The marker limits relaunch attempts; it must never override the actual job membership.
            try
            {
                if (Array.IndexOf(args, "--detached") >= 0)
                    throw new System.ComponentModel.Win32Exception("The search process still belongs to a closing job after relaunch.");
                DetachedProcess.Start(Environment.ProcessPath!, [.. args, "--detached"]);
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                Trace.TraceError("Could not leave the parent job: " + error.Message);
                Environment.ExitCode = 1;
            }
            return;
        }
        using var mutex = new Mutex(true, @"Local\" + GlobalSearchConfiguration.PipeName(profile), out var created);
        if (!created)
        {
            SearchHostClient.SendAsync(new(Array.IndexOf(args, "--background") >= 0 ? "status" : "show", ManagerPath: manager), profile).GetAwaiter().GetResult();
            return;
        }
        if (Array.IndexOf(args, "--background") >= 0 && Array.IndexOf(args, "--configure") < 0 && !GlobalSearchConfiguration.Load(profile).Enabled) return;
        // A small, mostly static palette does not need a dedicated GPU cache.
        System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var host = new SearchHost(app, profile, manager);
#if FILESMATE_UI_TEST
        if (Environment.GetEnvironmentVariable("FILESMATE_RANKING_SMOKE") == "1")
        {
            app.Dispatcher.BeginInvoke(new Action(async () => { await host.RunRankingSmokeAsync(); app.Shutdown(); }));
            app.Run();
            mutex.ReleaseMutex();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_ICON_SMOKE") == "1")
        {
            app.Dispatcher.BeginInvoke(new Action(async () => { await IconStyleSmoke.RunAsync(profile); app.Shutdown(); }));
            app.Run();
            mutex.ReleaseMutex();
            return;
        }
        if (Environment.GetEnvironmentVariable("FILESMATE_LOCALIZATION_SMOKE") == "1")
            app.Dispatcher.BeginInvoke(new Action(async () => { await host.RunLocalizationSmokeAsync(); app.Shutdown(); }));
#endif
        if (Array.IndexOf(args, "--background") < 0) host.Show();
        app.Run();
        mutex.ReleaseMutex();
    }
}

internal sealed partial class SearchHost : IDisposable
{
    private readonly Application _app;
    private readonly HotkeyWindow _source;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _icon;
    private PaletteWindow? _window;
    private readonly IGlobalSearchProvider _searchProvider;
    private int _hotkeyId;
    private string _status = "";
    private bool _stopping;
    public string Profile { get; }
    public string ManagerPath { get; private set; }
    public GlobalSearchSettings Settings { get; private set; }
    public bool Registered => _hotkeyId != 0;
    internal bool IsStopping => _stopping;

    public SearchHost(Application app, string profile, string manager)
    {
        _app = app;
        Profile = profile;
        ManagerPath = manager;
        var catalog = new WindowsApplicationCatalog(() => ManagerPath);
        _searchProvider = new LauncherSearchProvider(catalog, profile);
        _ = catalog.GetAsync(CancellationToken.None);
        Settings = GlobalSearchConfiguration.Load(profile);
        try { if (FeatureSetup.Load(Profile).Completed) GlobalSearchStartup.Synchronize(Settings, GlobalSearchStartup.HostForManager(ManagerPath), ManagerPath, Profile); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { _status = Loc.Get("Startup_SaveFailed") + error.Message; }
        _source = new HotkeyWindow(id => { if (id == _hotkeyId) { if (_window is { IsVisible: true, IsClosing: false }) _window.DismissAnimated(); else Show(); } });
        if (Settings.Enabled && SearchHotkey.TryParse(Settings.Hotkey, out var key) && RegisterHotKey(_source.Handle, 1, key.Modifiers | 0x4000, key.Key)) _hotkeyId = 1;
        if (Settings.Enabled && !Registered) _status = Loc.Get("Shortcut_InUseHint");
        _icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        var menu = new TrayMenu(Profile, () => Show(), () => Show(true), OpenManagerFromTray, Stop);
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = Loc.Get("Search_WindowTitle"), ContextMenuStrip = menu, Visible = true };
        long lastClick = 0;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left || Environment.TickCount64 - lastClick < Forms.SystemInformation.DoubleClickTime) return;
            lastClick = Environment.TickCount64;
            if (Settings.TrayLeftAction == "Files") OpenManagerFromTray(); else Show();
        };
        if (_status.Length > 0) _tray.ShowBalloonTip(5000, Loc.Get("Search_WindowTitle"), _status, Forms.ToolTipIcon.Warning);
        _ = ListenAsync();
        _app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            if (_stopping) return;
            _window ??= new PaletteWindow(this, _searchProvider);
            _window.Prepare();
        }));
    }

    private void OpenManagerFromTray()
    {
        try { OpenManager(); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        { _status = error.Message; Show(true); }
    }

    public void Show(bool settings = false)
    {
        _window ??= new PaletteWindow(this, _searchProvider);
        _window.Open(settings, _status);
    }
    public void WindowDismissed()
    {
        if (_stopping) return;
        if (!Settings.Enabled) Stop();
    }
    private void Stop() { _stopping = true; _app.Shutdown(); }

    public SearchHostReply Apply(GlobalSearchSettings settings)
    {
        if (!SearchHotkey.TryParse(settings.Hotkey, out var key)) return Reply(false, Loc.Get("Shortcut_ValidKeys"));
        settings = settings.Normalize();
        var same = _hotkeyId != 0 && SearchHotkey.TryParse(Settings.Hotkey, out var previous) && previous == key;
        var nextId = _hotkeyId == 1 ? 2 : 1;
        var acquired = settings.Enabled && !same;
        if (acquired && !RegisterHotKey(_source.Handle, nextId, key.Modifiers | 0x4000, key.Key))
        {
            var error = Marshal.GetLastWin32Error();
            return error == 1409
                ? Reply(false, Loc.Get("Shortcut_ChangeConflict"), "HotkeyConflict")
                : Reply(false, Loc.Format("Shortcut_RegisterFailed", error), "HotkeyRegistrationFailed");
        }
        try { GlobalSearchStartup.Save(settings, GlobalSearchStartup.HostForManager(ManagerPath), ManagerPath, Profile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            if (acquired) UnregisterHotKey(_source.Handle, nextId);
            return Reply(false, Loc.Get("Settings_SaveFailedDetail") + e.Message);
        }
        if (_hotkeyId != 0 && (!settings.Enabled || acquired)) UnregisterHotKey(_source.Handle, _hotkeyId);
        _hotkeyId = settings.Enabled ? acquired ? nextId : _hotkeyId : 0;
        Settings = settings;
        _window?.ApplyPreviewSetting();
        _status = "";
        return Reply(true, settings.Enabled ? Loc.Get("SavedPrefix") + settings.Hotkey + Loc.Get("Search_OpenSuffix") : Loc.Get("Search_ExitWhenHidden"));
    }

    public void OpenManager(string? path = null, bool select = false, bool searchSettings = false)
    {
        if (!File.Exists(ManagerPath)) throw new FileNotFoundException(Loc.Get("Search_ManagerMissing"), ManagerPath);
        var arguments = new List<string>();
        if (searchSettings) arguments.Add("--settings-search");
        else if (path is not null) { arguments.Add(select ? "/select," + path : "/open"); if (!select) arguments.Add(path); }
        else arguments.Add("--activate");
        // The file manager must not share our job either; otherwise closing whatever started the search host
        // (an installer, a terminal, an IDE) would also close every FilesMate window opened from the palette.
        DetachedProcess.Start(ManagerPath, arguments);
    }

    public void RunFileAction(string command, string[] paths)
    {
        if (!File.Exists(ManagerPath)) throw new FileNotFoundException(Loc.Get("Search_ManagerMissing"), ManagerPath);
        var id = new SearchFileAction(command, paths).Write();
        DetachedProcess.Start(ManagerPath, ["--search-action", id]);
    }

    private SearchHostReply Reply(bool ok = true, string? message = null, string? errorCode = null) => new(ok, message ?? _status, Registered, _window?.IsVisible == true, Environment.ProcessId, errorCode);

    private async Task ListenAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(GlobalSearchConfiguration.PipeName(Profile), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var message = await SearchHostProtocol.ReadAsync(reader, SearchHostProtocol.MaximumRequestCharacters, timeout.Token).ConfigureAwait(false);
                if (message is null) continue;
                var request = JsonSerializer.Deserialize<SearchHostRequest>(message);
                var response = await _app.Dispatcher.InvokeAsync(() =>
                {
                    if (request is null) return Reply(false, "Invalid request.");
                    if (request.Command == "stop-installation") return Reply(string.Equals(request.ManagerPath, ManagerPath, StringComparison.OrdinalIgnoreCase));
                    if (request.ManagerPath is { } manager && Path.GetFileName(manager).Equals("FilesMate.App.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(manager)) ManagerPath = manager;
                    if (request.Command == "show") Show();
                    if (request.Command == "settings") Show(true);
                    if (request.Command != "apply" || request.Settings is not { } settings) return Reply();
                    var applied = Apply(settings);
                    // Over the pipe there is no palette to dismiss first: a disable request stops the host right away
                    // (see below), so tell the caller what actually happens instead of the palette's deferred wording.
                    return applied.Ok && !settings.Enabled ? applied with { Message = Loc.Get("Search_ResidencyStopped") } : applied;
                });
                await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), timeout.Token).ConfigureAwait(false);
                if (request?.Command == "stop" || request?.Command == "stop-installation" && response.Ok || request?.Command == "apply" && response.Ok && request.Settings?.Enabled == false)
                    await _app.Dispatcher.InvokeAsync(Stop);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _lifetime.Cancel();
        if (_hotkeyId != 0) UnregisterHotKey(_source.Handle, _hotkeyId);
        _window?.CancelSearch();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _icon.Dispose();
        _source.Dispose();
        _lifetime.Dispose();
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

internal sealed class HotkeyWindow : Forms.NativeWindow, IDisposable
{
    private readonly Action<int> _pressed;
    public HotkeyWindow(Action<int> pressed)
    {
        _pressed = pressed;
        CreateHandle(new Forms.CreateParams { Caption = "FilesMate.GlobalSearch.Hotkey", Parent = new IntPtr(-3) });
    }
    protected override void WndProc(ref Forms.Message message)
    {
        if (message.Msg == 0x0312) _pressed(message.WParam.ToInt32());
        base.WndProc(ref message);
    }
    public void Dispose() => DestroyHandle();
}
