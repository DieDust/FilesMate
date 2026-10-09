using System.Runtime.CompilerServices;

namespace FilesMate.App.Diagnostics;

internal static class StartupBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Explorer-mediated independent launches do not inherit the caller's process
        // environment. An explicit opt-in diagnostic triplet survives that launch.
        var args = Environment.GetCommandLineArgs();
        try { FilesMate.Core.Configuration.RuntimeProfile.Initialize(args); }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { FilesMate.Core.Configuration.RuntimeProfile.InitializationError = error.Message; return; }
        var index = Array.IndexOf(args, "--diagnostic-trace");
        if (index >= 0 && index + 2 < args.Length && Path.IsPathFullyQualified(args[index + 1])
            && Guid.TryParse(args[index + 2], out _))
        {
            try { FilesMate.Core.Configuration.RuntimeProfile.RequireInside(args[index + 1]); }
            catch (ArgumentException error)
            { FilesMate.Core.Configuration.RuntimeProfile.InitializationError = error.Message; return; }
            Environment.SetEnvironmentVariable("FILESMATE_PR00_TRACE", "1");
            Environment.SetEnvironmentVariable("FILESMATE_PR00_TRACE_FILE", args[index + 1]);
            Environment.SetEnvironmentVariable("FILESMATE_PR00_RUN_ID", args[index + 2]);
        }
        StartupClock.Mark("ProcessEntry");
    }

    internal static IEnumerable<string> WithoutTraceArguments(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (args[index] == "--diagnostic-trace" && index + 2 < args.Count)
            { index += 2; continue; }
            yield return args[index];
        }
    }
}
