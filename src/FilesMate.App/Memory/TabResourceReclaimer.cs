using FilesMate.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;

namespace FilesMate.App.Memory;

/// <summary>One idle reclamation queue for all windows on the application's UI thread.</summary>
internal static class TabResourceReclaimer
{
    private static DispatcherQueueTimer? _timer;
    private static long _lastActivity, _lastCollection;
    private static long _lastDecommit;
    private static int _requestVersion;
    private static readonly List<WeakReference> RetiredPages = [];
    private static int _passes;
    private static int _previousSurvivors;
    internal static int Collections { get; private set; }
    internal static int ForcedCollections { get; private set; }
    internal static int DecommittingCollections { get; private set; }
    internal static double LastDecommitMilliseconds { get; private set; }
    internal static int RetiredResourcesAlive => RetiredPages.Count(reference => reference.IsAlive);

    internal static void NotifyActivity() => _lastActivity = Environment.TickCount64;

    internal static void Request(DispatcherQueue queue, IReadOnlyList<WeakReference> retiredResources)
    {
        if (_timer is null)
        {
            _timer = queue.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += async (_, _) => await ReclaimAsync();
        }
        NotifyActivity();
        _requestVersion++;
        RetiredPages.RemoveAll(reference => !reference.IsAlive);
        RetiredPages.AddRange(retiredResources);
        // Diagnostic references must not grow without bound even if a third-party
        // component retains retired pages. Never retain the actual page here.
        if (RetiredPages.Count > 256) RetiredPages.RemoveRange(0, RetiredPages.Count - 256);
        _passes = 0;
        _previousSurvivors = RetiredPages.Count;
        TraceReclaim("request");
        Arm(Math.Max(2000, 10000 - (Environment.TickCount64 - _lastCollection)));
    }

    private static void Arm(long milliseconds)
    {
        _timer!.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
        _timer.Start();
    }

    private static async Task ReclaimAsync()
    {
        var quiet = Environment.TickCount64 - _lastActivity;
        TraceReclaim("tick");
        if (quiet < 2000 || FileOperationLifetime.IsBusy || App.IsMemoryReclamationBusy)
        {
            Arm(Math.Max(500, 2000 - quiet));
            return;
        }
        RetiredPages.RemoveAll(reference => !reference.IsAlive);
        // A native release can make another layer of wrappers collectible. Allow
        // one final pass only when the previous pass demonstrably made progress.
        // A retained/live object must never cause an endless collection loop.
        if (RetiredPages.Count > 0 && (_passes < 2
            || _passes == 2 && RetiredPages.Count < _previousSurvivors))
        {
            _previousSurvivors = RetiredPages.Count;
            _lastCollection = Environment.TickCount64;
#if FILESMATE_UI_TEST
            var strategy = Environment.GetEnvironmentVariable("FILESMATE_MEMORY_COLLECTION");
            if (strategy != "natural")
#endif
            {
                var before = GC.CollectionCount(GC.MaxGeneration);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);
                // Small managed wrappers own large native trees. If the runtime
                // declines collection despite retired pages, request one pass.
                if (GC.CollectionCount(GC.MaxGeneration) == before)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
                    ForcedCollections++;
                }
                Collections++;
            }
            _passes++;
            TraceReclaim("collected");
            Arm(2000);
            return;
        }
        var requestVersion = _requestVersion;
        try
        {
            if (App.CurrentWindow?.Content is Microsoft.UI.Xaml.UIElement { XamlRoot: not null } root)
                await ElementCompositionPreview.GetElementVisual(root).Compositor.RequestCommitAsync();
            // Commit detached visuals before decommitting unused native blocks.
            await Task.Yield();
            // Input or another close may have arrived while the compositor was
            // committing. Its reclamation batch owns any new retired pages.
            if (requestVersion != _requestVersion) return;
            if (Environment.TickCount64 - _lastActivity < 2000
                || FileOperationLifetime.IsBusy || App.IsMemoryReclamationBusy)
            {
                Arm(2000);
                return;
            }
            var retryAfter = ReleaseUnusedManagedCapacity();
            FilesMate.Platform.Windows.Threading.NativeHeapResources.ReleaseUnused();
            TraceReclaim("native-released");
            if (retryAfter > 0) Arm(retryAfter);
            else _passes = 0;
        }
        catch (Exception error) { App.LogFailure("ReleaseTabVisuals", error); }
    }

    private static long ReleaseUnusedManagedCapacity()
    {
#if FILESMATE_UI_TEST
        if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_COLLECTION") == "natural") return 0;
#endif
        var now = Environment.TickCount64;
        var committed = GC.GetGCMemoryInfo().TotalCommittedBytes;
        var used = GC.GetTotalMemory(forceFullCollection: false);
        // Collecting WinRT wrappers releases the views, but a background GC may
        // leave the enlarged heap committed. Decommit only a substantially idle
        // heap, after a close batch; never poll or trim the process working set.
        // Keep large live datasets out of this blocking collection path.
        if (committed - used < 8L * 1024 * 1024 || used > committed - committed / 4
            || used > 64L * 1024 * 1024) return 0;
        // Coalesce another close during the cooldown, then reconsider once.
        // Keep the finished wrapper-pass count while waiting so this does not
        // start another series of full collections.
        if (_lastDecommit != 0 && now - _lastDecommit < 30000)
            return 30000 - (now - _lastDecommit);

        _lastDecommit = now;
        _lastCollection = now;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        LastDecommitMilliseconds = watch.Elapsed.TotalMilliseconds;
        DecommittingCollections++;
        TraceReclaim("managed-decommitted");
        return 0;
    }

    [System.Diagnostics.Conditional("FILESMATE_UI_TEST")]
    private static void TraceReclaim(string stage)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "tab-reclamation.jsonl"),
            System.Text.Json.JsonSerializer.Serialize(new { Time = DateTimeOffset.Now, Stage = stage,
                Passes = _passes, Alive = RetiredResourcesAlive, Quiet = Environment.TickCount64 - _lastActivity,
                FileBusy = FileOperationLifetime.IsBusy, UiBusy = App.IsMemoryReclamationBusy,
                DecommittingCollections, LastDecommitMilliseconds,
                Finalizers = GC.GetGCMemoryInfo().FinalizationPendingCount,
                Gen2 = GC.CollectionCount(GC.MaxGeneration), PrivateBytes = process.PrivateMemorySize64 }) + Environment.NewLine);
    }
}
