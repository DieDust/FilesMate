#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Navigation;
using FilesMate.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunTabMemorySmokeAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "tab-memory-smoke.json");
        var samples = new List<object>();
        var cycles = int.TryParse(Environment.GetEnvironmentVariable("FILESMATE_TAB_MEMORY_CYCLES"), out var requestedCycles)
            ? Math.Clamp(requestedCycles, 1, 25) : 5;
        var switchMilliseconds = new List<double>();
        var warmSwitchMilliseconds = new List<double>();
        var closed = new List<WeakReference>();
        var uiGaps = new List<double>();
        var heartbeat = DispatcherQueue.CreateTimer();
        heartbeat.Interval = TimeSpan.FromMilliseconds(20);
        var lastHeartbeat = Stopwatch.GetTimestamp();
        heartbeat.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            uiGaps.Add(Stopwatch.GetElapsedTime(lastHeartbeat, now).TotalMilliseconds);
            lastHeartbeat = now;
        };
        heartbeat.Start();
        var natural = Environment.GetEnvironmentVariable("FILESMATE_TAB_MEMORY_DIAGNOSTIC_GC") != "1";
        try
        {
            await Ready();
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_ATTRIBUTION") == "1")
            {
                await RunMemoryAttributionAsync();
                return;
            }
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_STATE_CHECK_ONLY") == "1")
            {
                await VerifyTabStateAsync();
                File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true }));
                return;
            }
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_TEMPLATE_CHECK_ONLY") == "1")
            {
                var released = await VerifyRetiredGridAsync();
                File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = true, ReleasedGridTiles = released }));
                return;
            }
            var path = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!.ViewModel.AddressText;
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_NAVIGATION_PROBE") == "1")
            {
                var locations = Environment.GetEnvironmentVariable("FILESMATE_MEMORY_PATHS")!.Split('|');
                await Sample("navigation-start");
                for (var cycle = 1; cycle <= cycles; cycle++)
                {
                    ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!.ViewModel.Navigate(locations[1]);
                    await Ready();
                    await Sample($"navigation-{cycle}-grid");
                    ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!.ViewModel.Navigate(locations[0]);
                    await Ready();
                    await OpenScenarioTabAsync(path, 1);
                    Tabs.SelectedItem = Tabs.TabItems[0];
                    CloseTrackedTabs(closed);
                    await Task.Delay(12000);
                    await Sample($"navigation-{cycle}-closed");
                }
                File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = closed.All(item => !item.IsAlive), Samples = samples }));
                return;
            }
            await Sample("one");
            for (var i = 1; i < 9; i++)
            {
                await OpenScenarioTabAsync(path, i);
                if (i is 4 or 8) await Sample(i == 4 ? "five" : "nine");
            }
            for (var i = 0; i < 9; i++)
            {
                var switchWatch = Stopwatch.StartNew();
                Tabs.SelectedItem = Tabs.TabItems[i];
                // Observe actual layout completion; the settle delay in Ready is excluded.
                await Task.Yield();
                if (TabHost.Content is FrameworkElement content) content.UpdateLayout();
                switchMilliseconds.Add(switchWatch.Elapsed.TotalMilliseconds);
                await Ready();
            }
            await Sample("revisited-nine");
            Tabs.SelectedItem = Tabs.TabItems[0];
            CloseTrackedTabs(closed);
            await Sample("closed-to-one");
            await Task.Delay(12000);
            await Sample("closed-after-idle");
            // Diagnostic collection distinguishes retained pages from pending finalizers.
            // This is compiled out of production; no working-set trimming is performed.
            for (var pass = 0; !natural && pass < 6; pass++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                // WinRT reference-tracker cleanup also needs a UI dispatcher turn.
                await Task.Delay(300);
                if (closed.All(reference => !reference.IsAlive)) break;
            }
            if (!natural) await Sample("closed-after-diagnostic-gc");
            var closedPagesAlive = closed.Count(reference => reference.IsAlive);
            for (var cycle = 1; cycle <= cycles; cycle++)
            {
                for (var i = 0; i < 8; i++)
                {
                    await OpenScenarioTabAsync(path, i);
                }
                Tabs.SelectedItem = Tabs.TabItems[0];
                CloseTrackedTabs(closed);
                if (natural) await Task.Delay(12000);
                await Sample($"cycle-{cycle}-closed");
            }
            for (var i = 0; i < 3; i++)
            {
                AddNavigatorTab(path);
                await Ready();
                EnableMemoryTestPreviewAndDualPane();
                await Task.Delay(700);
                CloseTrackedTabs(closed); // Also exercise closing the selected tab.
                await Ready();
            }
            await Task.Delay(natural ? 12000 : 3000);
            for (var pass = 0; !natural && pass < 6; pass++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                await Task.Delay(300);
            }
            closedPagesAlive = closed.Count(reference => reference.IsAlive);
            await Sample("cycles-settled");
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_TAIL_PROBE") == "1")
            {
                for (var pass = 1; pass <= 6; pass++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
                    await Task.Delay(2000);
                    FilesMate.Platform.Windows.Threading.NativeHeapResources.ReleaseUnused();
                    await Sample($"diagnostic-tail-{pass}");
                }
            }
            Require(closedPagesAlive == 0, $"{closedPagesAlive} closed pages remain alive");
            await VerifyTabStateAsync();
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_TEMPLATE_CHECK") == "1")
                await VerifyRetiredGridAsync();
            if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_EXTENDED") == "1")
                await VerifyMultiWindowReclamationAsync();
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Passed = true, Samples = samples, ClosedPagesAlive = closedPagesAlive, ClosedPagesTested = closed.Count,
                SelectionAndScrollPreserved = true, SidebarRestored = true,
                HomeAndPreviewAvailable = true, UnvisitedTabsDeferred = true,
                SwitchMilliseconds = switchMilliseconds, WarmSwitchMilliseconds = warmSwitchMilliseconds,
                MaxUiTickGapMilliseconds = uiGaps.Count == 0 ? 0 : uiGaps.Max()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), Samples = samples }));
        }

        finally { heartbeat.Stop(); }

        async Task OpenScenarioTabAsync(string fallback, int index)
        {
            var locations = Environment.GetEnvironmentVariable("FILESMATE_MEMORY_PATHS")?.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var destination = locations is { Length: > 0 } ? locations[index % locations.Length] : fallback;
            var homeFirst = Environment.GetEnvironmentVariable("FILESMATE_MEMORY_HOME_FIRST") == "1";
            AddNavigatorTab(homeFirst ? HomeLocation.Uri : destination);
            await Ready();
            if (homeFirst)
            {
                ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!.ViewModel.Navigate(destination);
                await Ready();
            }
        }

        async Task Ready()
        {
            for (var i = 0; i < 150; i++)
            {
                if (SelectedTabIsReady())
                {
                    if (Environment.GetEnvironmentVariable("FILESMATE_MEMORY_GRID") == "1")
                    {
                        var selectedPage = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!;
                        var fileSurface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(selectedPage)!;
                        fileSurface.SetLayout(FileLayoutKind.Grid);
                    }
                    var settle = int.TryParse(Environment.GetEnvironmentVariable("FILESMATE_MEMORY_SETTLE_MS"), out var milliseconds)
                        ? Math.Clamp(milliseconds, 250, 5000) : 250;
                    await Task.Delay(settle);
                    return;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("Tab did not load");
        }
        async Task VerifyTabStateAsync()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var first = (TabViewItem)Tabs.TabItems[0];
            var page = ((NavigatorTabContent)first.Tag).Navigator!;
            var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", flags)!.GetValue(page)!;
            var scroller = (ScrollViewer)typeof(FileDetailsSurface).GetField("Scroller", flags)!.GetValue(surface)!;
            var sidebar = typeof(NavigatorPage).GetField("Sidebar", flags)!.GetValue(page)!;
            var sidebarList = (ListView)sidebar.GetType().GetField("SectionRepeater", flags)!.GetValue(sidebar)!;
            Require(typeof(NavigatorPage).GetField("_previewHost", flags)!.GetValue(page) is null,
                "Preview was eagerly allocated");
            Require(typeof(NavigatorPage).GetField("_homeDashboard", flags)!.GetValue(page) is null,
                "Directory tab eagerly allocated home");
            var omni = (Controls.Omnibar.Omnibar)typeof(NavigatorPage).GetField("Omni", flags)!.GetValue(page)!;
            var pathBox = (TextBox)omni.GetType().GetField("PathBox", flags)!.GetValue(omni)!;
            Require(pathBox.Visibility == Visibility.Collapsed, "Inactive address editor participates in layout");
            omni.BeginPathEdit();
            Require(pathBox.Visibility == Visibility.Visible && pathBox.Text == page.ViewModel.AddressText,
                "Address editor did not become available");
            omni.CancelMode();
            Require(pathBox.Visibility == Visibility.Collapsed, "Address editor did not collapse after cancel");
            Require(surface.TrySelectByName("file12.txt"), "Fixture selection failed");
            await Task.Delay(300);
            scroller.ChangeView(null, 84, null, disableAnimation: true);
            await Task.Delay(200);
            var selected = surface.Selection.PrimaryId;
            var offset = scroller.VerticalOffset;
            AddNavigatorTab(HomeLocation.Uri);
            await Ready();
            Require(sidebarList.ItemsSource is null, "Inactive sidebar retained item templates");
            var repeater = (ItemsRepeater)typeof(FileDetailsSurface).GetField("Repeater", flags)!.GetValue(surface)!;
            Require(repeater.ItemsSource is not null && surface.ScrollOffset == offset,
                "Recent file list was rebuilt or lost scroll position");
            var homePage = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!;
            var home = (FrameworkElement)typeof(NavigatorPage).GetField("_homeDashboard", flags)!.GetValue(homePage)!;
            Require(home.Visibility == Visibility.Visible && home.ActualHeight > 0, "Home is not visible");
            Tabs.SelectedItem = first;
            await Ready();
            Require(surface.Selection.PrimaryId == selected && surface.Selection.Count == 1, "Tab switch lost selection");
            Require(Math.Abs(scroller.VerticalOffset - offset) < 1, "Tab switch lost scroll offset");
            Require(sidebarList.ItemsSource is not null, "Sidebar did not restore");
            var previewToggle = typeof(NavigatorPage).GetMethod("SetPreviewVisible", flags)!;
            previewToggle.Invoke(page, [true]);
            await Task.Delay(300);
            var preview = (FrameworkElement?)typeof(NavigatorPage).GetField("_previewHost", flags)!.GetValue(page);
            Require(preview is { IsLoaded: true, ActualWidth: > 0 }, "Lazy preview did not attach");
            previewToggle.Invoke(page, [false]);
            foreach (var item in Tabs.TabItems.OfType<TabViewItem>().Skip(1).ToArray()) CloseTab(item);
            // Queue multiple restored-style tabs before the dispatcher can construct any.
            for (var i = 0; i < 8; i++) AddNavigatorTab(page.ViewModel.AddressText);
            await Ready();
            Require(Tabs.TabItems.OfType<TabViewItem>().Count(item => ((NavigatorTabContent)item.Tag).Navigator is not null) == 2,
                "Unvisited background tabs allocated pages");
            Tabs.SelectedItem = first;
            foreach (var item in Tabs.TabItems.OfType<TabViewItem>().Skip(1).ToArray()) CloseTab(item);
            surface.SetLayout(FileLayoutKind.Grid);
            repeater = (ItemsRepeater)typeof(FileDetailsSurface).GetField("Repeater", flags)!.GetValue(surface)!;
            await Task.Delay(300);
            scroller.ChangeView(null, 100, null, disableAnimation: true);
            await Task.Delay(200);
            var gridOffset = surface.ScrollOffset;
            AddNavigatorTab(page.ViewModel.AddressText);
            await Ready();
            Require(repeater.ItemsSource is not null, "Recent grid was unnecessarily rebuilt");
            Tabs.SelectedItem = first;
            await Ready();
            Require(surface.LayoutKind == FileLayoutKind.Grid && surface.Selection.PrimaryId == selected
                && Math.Abs(surface.ScrollOffset - gridOffset) < 1,
                $"Grid tab lost presentation state: layout={surface.LayoutKind}, selection={surface.Selection.PrimaryId}/{selected}, scroll={surface.ScrollOffset}/{gridOffset}");
            for (var i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew();
                Tabs.SelectedItem = Tabs.TabItems[(i + 1) % 2];
                await Task.Yield();
                if (TabHost.Content is FrameworkElement visual) visual.UpdateLayout();
                warmSwitchMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                await Task.Delay(60);
            }
            Tabs.SelectedItem = first;
            AddNavigatorTab(page.ViewModel.AddressText);
            await Ready();
            AddNavigatorTab(page.ViewModel.AddressText);
            await Ready();
            Require(repeater.ItemsSource is null, "Old background view exceeded warm-tab limit");
            Tabs.SelectedItem = first;
            await Ready();
            Require(surface.Selection.PrimaryId == selected && Math.Abs(scroller.VerticalOffset - gridOffset) < 1,
                $"Evicted background view lost state: selection={surface.Selection.PrimaryId}/{selected}, scroll={surface.ScrollOffset}/{gridOffset}, extent={scroller.ScrollableHeight}");
            foreach (var item in Tabs.TabItems.OfType<TabViewItem>().Skip(1).ToArray()) CloseTab(item);
        }
        async Task VerifyMultiWindowReclamationAsync()
        {
            var path = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!.ViewModel.AddressText;
            var setting = Environment.GetEnvironmentVariable("FILESMATE_TAB_MEMORY_SMOKE");
            MainWindow other;
            try
            {
                Environment.SetEnvironmentVariable("FILESMATE_TAB_MEMORY_SMOKE", null);
                other = new MainWindow(new AppPageFactory(), new LaunchTarget(path, null));
            }
            finally { Environment.SetEnvironmentVariable("FILESMATE_TAB_MEMORY_SMOKE", setting); }
            App.TrackWindow(other);
            other.Activate();
            try
            {
                using (Services.FileOperationLifetime.Begin())
                {
                    for (var wait = 0; !other.SelectedTabIsReady() && wait < 150; wait++) await Task.Delay(100);
                    Require(other.SelectedTabIsReady(), "Second window did not load");
                    other.AddNavigatorTab(path);
                    for (var wait = 0; !other.SelectedTabIsReady() && wait < 150; wait++) await Task.Delay(100);
                    Require(other.SelectedTabIsReady(), "Second window tab did not load");
                    other.CloseTrackedTabs(closed);
                    AddNavigatorTab(path);
                    await Ready();
                    CloseTrackedTabs(closed);
                    var before = Memory.TabResourceReclaimer.Collections;
                    await Task.Delay(11000);
                    Require(Memory.TabResourceReclaimer.Collections == before, "Reclamation interrupted file work");
                }
                var idleStart = Memory.TabResourceReclaimer.Collections;
                for (var i = 0; i < 8; i++)
                {
                    Memory.TabResourceReclaimer.NotifyActivity();
                    await Task.Delay(500);
                }
                Require(Memory.TabResourceReclaimer.Collections == idleStart, "Reclamation ignored active input");
                await Task.Delay(12000);
                Require(Memory.TabResourceReclaimer.Collections - idleStart <= 3,
                    "Windows scheduled duplicate process-wide collections");
                Require(closed.All(reference => !reference.IsAlive), "Second window retained closed page");
                await Sample("multiwindow-reclaimed");
            }
            finally { other.Close(); Activate(); }
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        async Task Sample(string stage)
        {
            await Task.Delay(600);
            var buttonRight = NewTabButton.TransformToVisual(AppTitleBar)
                .TransformPoint(new Windows.Foundation.Point(NewTabButton.ActualWidth, 0)).X;
            var captionLeft = CaptionPad.TransformToVisual(AppTitleBar)
                .TransformPoint(new Windows.Foundation.Point(0, 0)).X;
            Require(buttonRight <= captionLeft, "New-tab button overlaps window controls");
            using var process = Process.GetCurrentProcess();
            var memory = new ProcessMemoryCounters { Size = 96 };
            if (!GetProcessMemoryInfo(process.Handle, ref memory, 96))
                throw new System.ComponentModel.Win32Exception();
            samples.Add(new { Stage = stage, Tabs = Tabs.TabItems.Count,
                TabCaptionGap = captionLeft - buttonRight,
                PrivateBytes = process.PrivateMemorySize64, WorkingSet = process.WorkingSet64,
                PrivateWorkingSet = memory.PrivateWorkingSet,
                ClosedPagesAlive = closed.Count(reference => reference.IsAlive),
                HandleCount = process.HandleCount,
                ReclamationRequests = Memory.TabResourceReclaimer.Collections,
                ForcedCollections = Memory.TabResourceReclaimer.ForcedCollections,
                RetiredResourcesAlive = Memory.TabResourceReclaimer.RetiredResourcesAlive,
                MaxUiTickGapMilliseconds = uiGaps.Count == 0 ? 0 : uiGaps.Max(),
                LastGcMaxPauseMilliseconds = GC.GetGCMemoryInfo().PauseDurations.ToArray().Select(pause => pause.TotalMilliseconds).DefaultIfEmpty().Max(),
                ImageCaches = Icons.ShellIconBinder.CacheStatistics,
                ManagedBytes = GC.GetTotalMemory(false), AllocatedBytes = GC.GetTotalAllocatedBytes(false),
                ManagedHeapCommittedBytes = GC.GetGCMemoryInfo().TotalCommittedBytes,
                TotalGcPauseMilliseconds = GC.GetTotalPauseDuration().TotalMilliseconds,
                RealizedItems = Tabs.TabItems.OfType<TabViewItem>().Sum(item =>
                {
                    var page = ((NavigatorTabContent)item.Tag).Navigator;
                    if (page is null) return 0;
                    var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                    return surface.RealizedCount;
                }) });
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tab-memory-progress.json"),
                JsonSerializer.Serialize(samples));
            uiGaps.Clear();
        }
    }

    private async Task<int> VerifyRetiredGridAsync()
    {
        var tiles = RetireGridLayoutForMemoryTest();
        if (tiles.Length == 0) throw new InvalidOperationException("Grid retirement test did not realize any tiles");
        for (var pass = 0; pass < 4 && tiles.Any(item => item.IsAlive); pass++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
            await Task.Delay(2000);
        }
        if (tiles.Any(item => item.IsAlive)) throw new InvalidOperationException("Details view retained the unused grid's recycled tiles");
        return tiles.Length;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference[] RetireGridLayoutForMemoryTest()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!;
        var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", flags)!.GetValue(page)!;
        surface.SetLayout(FileLayoutKind.Grid);
        surface.UpdateLayout();
        var tiles = (System.Collections.IEnumerable)typeof(FileDetailsSurface).GetField("_tiles", flags)!.GetValue(surface)!;
        var retired = tiles.Cast<object>().Select(tile => new WeakReference(tile)).ToArray();
        surface.SetLayout(FileLayoutKind.Details);
        surface.UpdateLayout();
        return retired;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private bool SelectedTabIsReady() =>
        Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { IsLoaded: true } page } }
        && !page.ViewModel.IsLoading;


    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size, PageFaultCount;
        public ulong PeakWorkingSet, WorkingSet, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage,
            PrivateUsage, PrivateWorkingSet, SharedCommitUsage;
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCounters counters, uint size);


    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void EnableMemoryTestPreviewAndDualPane()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!;
        typeof(NavigatorPage).GetMethod("SetDualPane", flags)!.Invoke(page, [true, false]);
        var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", flags)!.GetValue(page)!;
        if (!surface.TrySelectByName("file12.txt")) throw new InvalidOperationException("Preview fixture missing");
        typeof(NavigatorPage).GetMethod("SetPreviewVisible", flags)!.Invoke(page, [true]);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void CloseTrackedTabs(List<WeakReference> closed)
    {
        // Keep page temporaries out of the async test state machine during collection.
        foreach (var item in Tabs.TabItems.OfType<TabViewItem>().Skip(1).ToArray())
        {
            var page = ((NavigatorTabContent)item.Tag).Navigator!;
            closed.Add(new WeakReference(page));
            CloseTab(item);
            if (page.Content is not null || page.ViewModel.ViewIndex is not null)
                throw new InvalidOperationException("Closed tab retained its visual tree or folder index");
        }
    }
}
#endif
