using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Platform.Windows.Processes;
using FilesMate.Search;

namespace FilesMate.App.Services;

public static class GlobalSearchService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string HostPath => Path.Combine(AppContext.BaseDirectory, "SearchHost", "FilesMate.SearchHost.exe");

    public static async Task<SearchHostReply> EnsureStartedAsync(bool show = false)
    {
        await Gate.WaitAsync();
        try
        {
            if (!show && !FeatureSetup.Load().Completed) return new(true, Loc.Get("Search_AwaitingSetup"));
            if (!show && !GlobalSearchConfiguration.Load().Enabled)
            {
                GlobalSearchStartup.Synchronize(GlobalSearchConfiguration.Load(), HostPath, Environment.ProcessPath!);
                return new(true, Loc.Get("Search_BackgroundOff"));
            }
            return await ConnectOrStartAsync(show ? "show" : "status", configure: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { return new(false, Loc.Get("Search_StartFailed") + e.Message); }
        finally { Gate.Release(); }
    }

    public static async Task<SearchHostReply> ApplyAsync(GlobalSearchSettings settings)
    {
        if (!SearchHotkey.TryParse(settings.Hotkey, out _)) return new(false, Loc.Get("Shortcut_ValidKeys"));
        await Gate.WaitAsync();
        try
        {
            var running = await SearchHostClient.StatusAsync();
            if (running is null && !settings.Enabled)
            {
                GlobalSearchStartup.Save(settings, HostPath, Environment.ProcessPath!);
                return new(true, Loc.Get("Search_BackgroundOff"));
            }
            if (running is null)
            {
                var start = await ConnectOrStartAsync("status", configure: true);
                if (!start.Ok) return start;
            }
            var reply = await SearchHostClient.SendAsync(new("apply", settings, Environment.ProcessPath));
            if (reply is { Ok: false } && !GlobalSearchConfiguration.Load().Enabled) await SearchHostClient.SendAsync(new("stop"));
            return reply ?? new(false, Loc.Get("Search_NotResponding"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { return new(false, Loc.Get("Settings_SaveFailedDetail") + e.Message); }
        finally { Gate.Release(); }
    }

    /// <summary>Current host status without starting anything; <c>null</c> when no host is running.</summary>
    public static Task<SearchHostReply?> StatusAsync() => SearchHostClient.StatusAsync();

    internal static async Task RestartForLanguageAsync(string? profile = null)
    {
        await Gate.WaitAsync();
        try
        {
            profile ??= GlobalSearchConfiguration.DefaultDirectory;
            var running = await SearchHostClient.StatusAsync(profile);
            if (running is not { Pid: > 0 }) return; // A language change must not enable background search.
            if (!File.Exists(HostPath)) throw new FileNotFoundException(Loc.Get("Search_ComponentMissing"), HostPath);
            System.Diagnostics.Process previous;
            try { previous = System.Diagnostics.Process.GetProcessById(running.Pid); }
            catch (ArgumentException) { return; }
            using (previous)
            {
                var stopped = await SearchHostClient.SendAsync(new("stop-installation", ManagerPath: Environment.ProcessPath), profile);
                if (stopped is not { Ok: true }) throw new IOException(Loc.Get("Search_NotResponding"));
                await previous.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            DetachedProcess.Start(HostPath,
                [running.Visible ? "--show" : "--background", "--configure", "--profile", profile, "--manager", Environment.ProcessPath!], hidden: true);
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(150);
                if (await SearchHostClient.StatusAsync(profile) is { Ok: true }) return;
            }
            throw new IOException(Loc.Get("Search_StartTimeout"));
        }
        finally { Gate.Release(); }
    }

    private static async Task<SearchHostReply> ConnectOrStartAsync(string command, bool configure)
    {
        var request = new SearchHostRequest(command, ManagerPath: Environment.ProcessPath);
        var reply = SearchHostClient.IsRunning() ? await SearchHostClient.SendAsync(request) : null;
        if (reply is not null) return reply;
        if (!File.Exists(HostPath)) return new(false, Loc.Get("Search_ComponentMissing"));
        var arguments = new List<string> { command == "show" ? "--show" : "--background" };
        if (configure) arguments.Add("--configure");
        arguments.Add("--manager");
        arguments.Add(Environment.ProcessPath!);
        // The host outlives this process; it must not inherit a job that ends with us or with whatever ran us.
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        DetachedProcess.Start(HostPath, arguments, hidden: true);
        // A host that still had to re-launch itself out of a job needs a second process start before it listens.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(150);
            reply = await SearchHostClient.SendAsync(new("status", ManagerPath: Environment.ProcessPath));
            if (reply is not null) return reply;
        }
        return new(false, Loc.Get("Search_StartTimeout"));
    }
}
