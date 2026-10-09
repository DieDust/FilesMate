using System.Runtime.InteropServices;

using FilesMate.App.Navigation;

using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FilesMate.App;

/// <summary>
/// Single-instance routing via Windows App SDK AppInstance, following the Files app pattern.
/// </summary>
internal static partial class AppLifecycle
{
    private static string InstanceKey => FilesMate.Core.Configuration.RuntimeProfile.ScopedName("FilesMate.Main");
    private static string IncomingEventName => FilesMate.Core.Configuration.RuntimeProfile.ScopedName(@"Local\FilesMate.IncomingLaunch");
    private const int AsfwAny = -1;
    private const int SwRestore = 9;

    private static EventWaitHandle? _incoming;

    public static bool TryBecomeMainInstance()
    {
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            RedirectActivationTo(instance);
            return false;
        }

        _mainInstance = instance;
        instance.Activated += Instance_Activated;
        _incoming = new EventWaitHandle(false, EventResetMode.AutoReset, IncomingEventName);
        _incomingWait = ThreadPool.RegisterWaitForSingleObject(_incoming, OnIncoming, null, -1, executeOnlyOnce: false);
        return true;
    }

    public static LaunchTarget PendingLaunch { get; private set; } = LaunchPath.Parse(FilesMate.Core.Configuration.RuntimeProfile.WithoutProfileArguments(Environment.GetCommandLineArgs()).ToArray());

    public static void SetPendingLaunch(LaunchTarget target) => PendingLaunch = target;

    public static void TraceLaunch(string source, IReadOnlyList<string> args, LaunchTarget target)
    {
        try
        {
            var directory = Path.GetDirectoryName(Program.SettingsPath(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "launch.log")))!;
            Directory.CreateDirectory(directory);
            var line = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[{0:O}] {1} cmd={2} folder={3} select={4}{5}",
                DateTimeOffset.Now,
                source,
                Environment.CommandLine,
                target.Folder ?? "",
                target.SelectPath ?? "",
                Environment.NewLine);
            File.AppendAllText(Path.Combine(directory, "launch.log"), line);
        }
        catch
        {
        }
    }

    public static void TraceInternal(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(Program.SettingsPath(Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath, "launch.log")))!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "launch.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public static IEnumerable<LaunchTarget> TakePendingRedirects() => ClaimPendingRedirects();

    public static void TryBringToForeground(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            _ = ShowWindow(hwnd, SwRestore);
        }

        _ = SetForegroundWindow(hwnd);
    }

    private static void Instance_Activated(object? sender, AppActivationArguments args)
    {
        var targets = ResolveRedirectedTargets(args);
        foreach (var target in targets)
        {
            TraceLaunch("Activated", Environment.GetCommandLineArgs(), target);
        }

        if (Application.Current is App app)
        {
            foreach (var target in targets)
            {
                app.HandleRedirectedLaunch(target);
            }

            return;
        }

        foreach (var target in targets) EarlyIncoming.Enqueue(target);
    }

    private static List<LaunchTarget> ResolveRedirectedTargets(AppActivationArguments args)
    {
        var pending = ClaimPendingRedirects();
        if (pending.Count > 0)
        {
            return pending;
        }

        return [LaunchPath.Parse(ReadLaunchArgs(args))];
    }

    private static IReadOnlyList<string> ReadLaunchArgs(AppActivationArguments args)
    {
        var lines = new List<string> { Environment.ProcessPath ?? "FilesMate.App.exe" };
        try
        {
            switch (args.Kind)
            {
                case ExtendedActivationKind.Launch
                    when args.Data is Windows.ApplicationModel.Activation.LaunchActivatedEventArgs launch
                    && !string.IsNullOrWhiteSpace(launch.Arguments):
                    lines.AddRange(LaunchPath.SplitActivationArguments(launch.Arguments));
                    break;
                case ExtendedActivationKind.File
                    when args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs files:
                    foreach (var file in files.Files)
                    {
                        if (!string.IsNullOrWhiteSpace(file.Path))
                        {
                            lines.Add(file.Path);
                        }
                    }

                    break;
                case ExtendedActivationKind.Protocol
                    when args.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol
                    && protocol.Uri is not null:
                    lines.Add(protocol.Uri.OriginalString);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Activation args read failed: {0}", ex);
        }

        return LaunchPath.NormalizeLaunchArgs(FilesMate.Core.Configuration.RuntimeProfile.WithoutProfileArguments(lines).Select(argument => argument == RecoveryArgument ? "--activate" : argument).ToArray());
    }

    private static void OnIncoming(object? state, bool timedOut)
    {
        if (Application.Current is not App app)
        {
            return;
        }

        foreach (var target in TakePendingRedirects())
        {
            TraceLaunch("Incoming", Environment.GetCommandLineArgs(), target);
            try
            {
                app.HandleRedirectedLaunch(target);
            }
            catch (Exception error)
            {
                App.LogFailure("IncomingActivation", error);
            }
        }
    }

    private static void RedirectActivationTo(AppInstance instance)
    {
        if (!Program.IsActivationRecovery)
            PendingLaunchExchange.Enqueue(FilesMate.Core.Configuration.RuntimeProfile.WithoutProfileArguments(Environment.GetCommandLineArgs()).ToArray());
        _ = AllowSetForegroundWindow(AsfwAny);
        if (EventWaitHandle.TryOpenExisting(IncomingEventName, out var incoming))
        {
            using (incoming)
            {
                if (incoming.Set())
                {
                    // The queued payload has one consumer; a second COM redirect
                    // would turn a single click into minimize followed by restore.
                    return;
                }
            }
        }

        // Use the SDK redirect only when the main instance's event is not ready.
        try
        {
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            _ = instance.RedirectActivationToAsync(args);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Activation redirect failed: {0}", ex);
        }

        Environment.Exit(0);
    }

    private static class PendingLaunchExchange
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

        private static string DirectoryPath => Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
            "pending-launches");

        public static void Enqueue(IReadOnlyList<string> args)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"{DateTime.UtcNow.Ticks:D20}-{Guid.NewGuid():N}.txt");
            File.WriteAllLines(path, args);
        }

        public static void EnqueueTarget(LaunchTarget target)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"{DateTime.UtcNow.Ticks:D20}-{Guid.NewGuid():N}.activation.json");
            var temporary = path + ".new";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(target));
            File.Move(temporary, path);
        }

        public static List<LaunchTarget> DequeueAll()
        {
            var result = new List<LaunchTarget>();
            if (!Directory.Exists(DirectoryPath))
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(DirectoryPath, "*.txt").Concat(Directory.GetFiles(DirectoryPath, "*.activation.json")).OrderBy(item => item, StringComparer.Ordinal))
            {
                try
                {
                    var age = DateTime.UtcNow - File.GetCreationTimeUtc(file);
                    if (age > MaxAge && !file.EndsWith(".activation.json", StringComparison.Ordinal))
                    {
                        File.Delete(file);
                        continue;
                    }

                    var target = file.EndsWith(".activation.json", StringComparison.Ordinal)
                        ? System.Text.Json.JsonSerializer.Deserialize<LaunchTarget>(File.ReadAllText(file))
                        : LaunchPath.Parse(File.ReadAllLines(file));
                    if (target is not null) result.Add(target);
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (System.Text.Json.JsonException error)
                { App.LogFailure("ActivationPayloadRead", error); }
            }

            return result;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
