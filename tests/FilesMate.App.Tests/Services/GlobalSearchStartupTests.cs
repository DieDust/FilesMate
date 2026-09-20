using FilesMate.Search;
using Microsoft.Win32;

namespace FilesMate.App.Tests.Services;

public sealed class GlobalSearchStartupTests
{
    [Fact]
    public void Startup_command_quotes_spaces_and_does_not_duplicate_the_install_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate startup 测试", new string('x', 90));
        var host = Path.Combine(root, "SearchHost", "FilesMate.SearchHost.exe");
        var command = GlobalSearchStartup.Command(host, Path.Combine(root, "FilesMate.App.exe"));
        Assert.Equal(new[] { host, "--background", "--startup" }, FilesMate.App.Navigation.LaunchPath.SplitActivationArguments(command));
        Assert.True(command.Length <= 260);
    }

    [Fact]
    public void Legacy_settings_default_to_startup_and_explicit_opt_out_is_preserved()
    {
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        try
        {
            File.WriteAllText(GlobalSearchConfiguration.SettingsPath(profile), """{"Enabled":true,"Hotkey":"Alt+Space"}""");
            Assert.True(GlobalSearchConfiguration.Load(profile).StartAtLogin);
            GlobalSearchConfiguration.Save(new(true, "Alt+Space", "Files", false), profile);
            var saved = GlobalSearchConfiguration.Load(profile);
            GlobalSearchConfiguration.Save(saved with { Hotkey = "Ctrl+K" }, profile);
            Assert.False(GlobalSearchConfiguration.Load(profile).StartAtLogin);
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public void Startup_registration_respects_settings_rolls_back_failed_saves_and_removes_only_its_own_installation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-startup-" + Guid.NewGuid().ToString("N"));
        var registryPath = @"Software\FilesMate.Tests\Startup-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(profile);
        try
        {
            string? Registered() { using var key = Registry.CurrentUser.OpenSubKey(registryPath); return key?.GetValue(GlobalSearchStartup.ValueName) as string; }
            var host = Environment.ProcessPath!;
            var settings = new GlobalSearchSettings();
            GlobalSearchStartup.Save(settings, host, host, profile, registryPath);
            var command = Registered();
            Assert.NotNull(command);
            Assert.Contains("--background --startup", command);
            Assert.Equal(settings, GlobalSearchConfiguration.Load(profile));
            using (var locked = File.Open(GlobalSearchConfiguration.SettingsPath(profile), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Record.Exception(() => GlobalSearchStartup.Save(settings with { StartAtLogin = false }, host, host, profile, registryPath));
                Assert.True(error is IOException or UnauthorizedAccessException);
                Assert.Equal(command, Registered());
                Assert.True(GlobalSearchConfiguration.Load(profile).StartAtLogin);
            }
            GlobalSearchStartup.Save(settings with { StartAtLogin = false }, host, host, profile, registryPath);
            Assert.Null(Registered());
            GlobalSearchStartup.Synchronize(GlobalSearchConfiguration.Load(profile), host, host, profile, registryPath);
            Assert.Null(Registered()); // Reopening/upgrading must retain opt-out.
            GlobalSearchStartup.Save(settings with { Enabled = false }, host, host, profile, registryPath);
            Assert.Null(Registered()); // No listener should start when residency is off.
            GlobalSearchStartup.Save(settings, host, host, profile, registryPath);
            GlobalSearchStartup.RemoveOwned(host, Path.Combine(profile, "Other.exe"), profile, registryPath);
            Assert.Equal(command, Registered());
            GlobalSearchStartup.RemoveOwned(host, host, profile, registryPath);
            Assert.Null(Registered());
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); Directory.Delete(profile, true); }
    }

    [Fact]
    public async Task Host_liveness_probe_answers_from_the_host_mutex_without_touching_the_pipe()
    {
        if (!OperatingSystem.IsWindows()) return;
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-probe-" + Guid.NewGuid().ToString("N"));
        Assert.False(SearchHostClient.IsRunning(profile));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await SearchHostClient.StatusAsync(profile));
        Assert.True(clock.ElapsedMilliseconds < 500, "an absent host must be reported immediately, not after the pipe timeout");
        using (var held = new Mutex(true, @"Local\" + GlobalSearchConfiguration.PipeName(profile), out var created))
        {
            Assert.True(created);
            Assert.True(SearchHostClient.IsRunning(profile));
            held.ReleaseMutex();
        }
        Assert.False(SearchHostClient.IsRunning(profile));
    }

    [Fact]
    public void Isolated_profiles_never_register_with_Windows_startup()
    {
        if (!OperatingSystem.IsWindows()) return;
        string? Read() { using var key = Registry.CurrentUser.OpenSubKey(GlobalSearchStartup.RunKey); return key?.GetValue(GlobalSearchStartup.ValueName) as string; }
        var before = Read();
        GlobalSearchStartup.Synchronize(new(), Environment.ProcessPath!, Environment.ProcessPath!, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Equal(before, Read());
    }
}
