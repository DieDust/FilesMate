using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.Processes;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FilesMate.App;

public static class Program
{
    internal static LanguageRestartSession? LanguageRestart { get; private set; }
    internal static bool ProfileWasNew { get; private set; }
    internal static bool IsActivationRecovery { get; } = Environment.GetCommandLineArgs().Contains(AppLifecycle.RecoveryArgument, StringComparer.Ordinal);
    internal static bool UseNativeShellHost { get; } =
        !Environment.GetCommandLineArgs().Contains("--no-native-shell", StringComparer.OrdinalIgnoreCase)
        && (Environment.GetCommandLineArgs().Contains("--native-shell", StringComparer.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(AppContext.BaseDirectory, "shell-compatibility.enabled")));
    internal static string SettingsPath(string path) => IsUiTestBuild
        ? Path.Combine(AppContext.BaseDirectory, "test-profile", Path.GetFileName(path))
        : FilesMate.Core.Configuration.RuntimeProfile.MapSettingsPath(path);

    internal static bool IsUiTestBuild =>
#if FILESMATE_UI_TEST
        true;
#else
        false;
#endif

    [STAThread]
    public static void Main(string[] args)
    {
        if (FilesMate.Core.Configuration.RuntimeProfile.InitializationError is not null)
        {
            if (!args.Contains(FilesMate.Core.Configuration.ProfileContext.Argument, StringComparer.OrdinalIgnoreCase))
                Diagnostics.ProfileStartupError.Show(FilesMate.Core.Configuration.RuntimeProfile.InitializationError);
            Environment.ExitCode = 2; return;
        }
        ProfileWasNew = !FilesMate.Search.FeatureTourHistory.HasExistingUserData(
            FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
            isolatedPlacementMetadata: FilesMate.Core.Configuration.RuntimeProfile.HasPrivateProfile);
        args = FilesMate.Core.Configuration.RuntimeProfile.WithoutProfileArguments(args).ToArray();
        if (args is ["--test-profile-check"] && FilesMate.Core.Configuration.RuntimeProfile.IsIsolated)
        {
            File.WriteAllText(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "profile-check-app.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    Profile = FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
                    Language = SettingsPath(Localization.LanguageSettings.DefaultFilePath),
                    Metadata = SettingsPath(SqliteFileMetadataStore.DefaultFilePath),
                    Database = FilesMate.Search.GlobalSearchConfiguration.ResolveDatabase(),
                    Pipe = FilesMate.Search.GlobalSearchConfiguration.PipeName(),
                    UpdatesCache = SettingsPath(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "Updates", "cache")),
                    RestartArguments = FilesMate.Core.Configuration.RuntimeProfile.ForChild(["--language-restart", "check"]),
                    PersistentIntegration = false,
                }));
            return;
        }
        // Installed copies may be started by a terminal, installer or development tool. Leave its closing
        // job before taking the single-instance lock or consuming a language-restart session.
        if (!IsUiTestBuild && args is not ["--unregister-folder-handler"] && DetachedProcess.IsInKillOnCloseJob())
        {
            try
            {
                if (Array.IndexOf(args, "--detached") >= 0)
                    throw new System.ComponentModel.Win32Exception("The file manager still belongs to a closing job after relaunch.");
                DetachedProcess.Start(Environment.ProcessPath!, FilesMate.Core.Configuration.RuntimeProfile.ForChild([.. args, "--detached"]));
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                App.LogFailure("IndependentLaunch", error);
                Environment.ExitCode = 1;
            }
            return;
        }
        var restartIndex = Array.IndexOf(args, "--language-restart");
        if (restartIndex >= 0)
        {
            try
            {
                if (restartIndex + 1 >= args.Length) return;
                var profile = Path.GetDirectoryName(SettingsPath(Localization.LanguageSettings.DefaultFilePath))!;
                var token = args[restartIndex + 1];
                var state = LanguageRestartSession.Load(profile, token);
                try
                {
                    using var previous = System.Diagnostics.Process.GetProcessById(state.ParentPid);
                    if (previous.StartTime.ToUniversalTime().Ticks == state.ParentStartedUtcTicks && !previous.WaitForExit(60000)) return;
                }
                catch (ArgumentException) { /* The parent has already exited. */ }
                File.Delete(LanguageRestartSession.FilePath(profile, token));
                LanguageRestart = state;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                App.LogFailure("LanguageRestart", error);
                return;
            }
        }
        if (args is ["--unregister-folder-handler"])
        {
            try
            {
                new DefaultFolderAssociation(new CurrentUserRegistry())
                    .UnregisterInstallation(Environment.ProcessPath!);
            }
            catch (Exception)
            {
                Environment.ExitCode = 1;
            }

            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var argv = Diagnostics.StartupBootstrap.WithoutTraceArguments(FilesMate.Core.Configuration.RuntimeProfile.WithoutProfileArguments(Environment.GetCommandLineArgs()).ToArray()).Where(arg =>
            !arg.Equals("--detached", StringComparison.Ordinal)
            && !arg.Equals("--native-shell", StringComparison.OrdinalIgnoreCase)
            && !arg.Equals("--no-native-shell", StringComparison.OrdinalIgnoreCase) && !arg.Equals(AppLifecycle.RecoveryArgument, StringComparison.Ordinal)).ToArray();
        TryParkWorkingDirectory();
        var launch = LanguageRestart is not null ? new LaunchTarget(null, null, "general") : IsActivationRecovery ? new LaunchTarget(null, null, ActivateOnly: true) : LaunchPath.Parse(argv);
        AppLifecycle.TraceLaunch("Main", argv, launch);
        AppLifecycle.SetPendingLaunch(launch);
        if (!IsUiTestBuild && ExplorerPreferencesService.LoadOpenInExistingWindow()
            && !AppLifecycle.TryBecomeMainInstance())
        {
            return;
        }

        try
        {
            SqliteRuntime.InitializeBeforeUi();
            Diagnostics.StartupClock.Mark("DatabaseRuntimeReady");
            AppLifecycle.TraceInternal("DatabaseRuntime ready before XAML startup");
        }
        catch (Exception error)
        {
            App.LogFailure("DatabaseRuntimeStartup", error);
            AppLifecycle.EndMainInstance();
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            AppLifecycle.TraceInternal("Entering XAML application startup");
            Application.Start(_ =>
            {
                try
                {
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    AppLifecycle.TraceInternal("Constructing App");
                    new App();
                    AppLifecycle.TraceInternal("App constructed");
                }
                catch (Exception error) { App.LogFailure("AppConstruction", error); throw; }
            });
            AppLifecycle.TraceInternal("XAML application startup returned");
        }
        catch (Exception error) { App.LogFailure("XamlStartup", error); throw; }
        finally { AppLifecycle.EndMainInstance(); }
    }

    private static void TryParkWorkingDirectory()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.SetCurrentDirectory(root);
            }
        }
        catch (Exception)
        {
        }
    }
}
