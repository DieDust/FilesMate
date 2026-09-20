#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Memory;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunMemoryAttributionAsync()
    {
        var snapshots = new List<object>();
        var retired = new List<WeakReference>();
        var output = Path.Combine(AppContext.BaseDirectory, "memory-attribution.json");
        async Task Checkpoint(string stage)
        {
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_ATTRIBUTION_PAUSE") != "1") return;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "memory-checkpoint.txt"), stage);
            var resume = Path.Combine(AppContext.BaseDirectory, "continue-" + stage);
            while (!File.Exists(resume)) await Task.Delay(200);
        }
        async Task Record(string stage)
        {
            var snapshot = MemoryAttributionProbe.Capture();
            snapshots.Add(new { Stage = stage, Tabs = Tabs.TabItems.Count,
                ClosedPagesAlive = retired.Count(reference => reference.IsAlive), Snapshot = snapshot });
            File.WriteAllText(output, JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
            await Task.Delay(300);
        }
        try
        {
            while (!SelectedTabIsReady()) await Task.Delay(100);
            await Task.Delay(3000);
            await Record("cold");
            // Repeat the observer before browsing to expose its own one-time cost.
            await Task.Delay(3000);
            await Record("observer-warmed");
            await Checkpoint("cold");
            var first = (TabViewItem)Tabs.TabItems[0];
            var paths = Environment.GetEnvironmentVariable("FILESMATE_MEMORY_PATHS")!.Split('|');
            for (var round = 1; round <= 3; round++)
            {
                for (var i = 0; i < 4; i++)
                {
                    AddNavigatorTab(paths[i % paths.Length]);
                    while (!SelectedTabIsReady()) await Task.Delay(100);
                    await Task.Delay(1500);
                }
                await Record($"round-{round}-open");
                Tabs.SelectedItem = first;
                CloseTrackedTabs(retired);
                await Task.Delay(15000);
                await Record($"round-{round}-closed");
            }
            await Task.Delay(15000);
            await Record("settled");
            await Checkpoint("settled");
            Icons.ShellIconBinder.ClearCache();
            await Task.Delay(500);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            await Task.Delay(3000);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            await Task.Delay(3000);
            await Record("diagnostic-cache-clear-and-compaction");
            FilesMate.Platform.Windows.Threading.NativeHeapResources.ReleaseUnused();
            await Task.Delay(2000);
            await Record("diagnostic-native-decommit");
            var aggressive = System.Diagnostics.Stopwatch.StartNew();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "aggressive-gc-milliseconds.txt"), aggressive.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await Task.Delay(3000);
            await Record("diagnostic-aggressive-gc");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "memory-attribution-error.txt"), error.ToString());
        }
    }
}
#endif
