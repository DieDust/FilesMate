using System.Diagnostics;
using System.Text.Json;

namespace FilesMate.App.Diagnostics;

public readonly record struct StartupMark(string Name, long ElapsedMilliseconds);

/// <summary>
/// In-memory startup timestamps. Profile builds write them next to the executable on first frame.
/// </summary>
public static class StartupClock
{
    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();
    private static readonly List<StartupMark> Marks = [];
    private static readonly object Gate = new();
    private static bool _exported;

    public static void Mark(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var elapsed = Stopwatch.GetElapsedTime(StartTimestamp);
        lock (Gate)
        {
            Marks.Add(new StartupMark(name, (long)elapsed.TotalMilliseconds));
        }
        // Preserve the historic marks. FirstFrame remains a Loaded lifecycle proxy.
        if (FilesMate.Core.Diagnostics.PerformanceTrace.Enabled)
        {
            var stage = name switch
            {
                "ProcessEntry" => FilesMate.Core.Diagnostics.PerformanceStage.ManagedEntry,
                "DatabaseRuntimeReady" => FilesMate.Core.Diagnostics.PerformanceStage.DatabaseRuntimeReady,
                "AppConstructor" => FilesMate.Core.Diagnostics.PerformanceStage.AppConstructor,
                "WindowConstructed" => FilesMate.Core.Diagnostics.PerformanceStage.WindowConstructed,
                "WindowActivated" => FilesMate.Core.Diagnostics.PerformanceStage.WindowActivated,
                "FirstFrame" => FilesMate.Core.Diagnostics.PerformanceStage.LoadedProxy,
                _ => (FilesMate.Core.Diagnostics.PerformanceStage?)null
            };
            if (stage is { } value) FilesMate.Core.Diagnostics.PerformanceTrace.Record(value);
        }
    }

    public static IReadOnlyList<StartupMark> Snapshot()
    {
        lock (Gate)
        {
            return [.. Marks];
        }
    }

    public static void ExportIfProfile()
    {
#if PROFILE
        Export(Path.Combine(AppContext.BaseDirectory, "diagnostics", "startup-marks.json"));
#endif
    }

    public static void Export(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        lock (Gate)
        {
            if (_exported)
            {
                return;
            }

            _exported = true;
            var json = JsonSerializer.Serialize(Marks);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
        }
    }
}
