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
        var switchMilliseconds = new List<double>();
        try
        {
            await Ready();
            var path = ((NavigatorTabContent)((TabViewItem)Tabs.SelectedItem).Tag).Navigator!.ViewModel.AddressText;
            await Sample("one");
            for (var i = 1; i < 9; i++)
            {
                AddNavigatorTab(path);
                await Ready();
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
            var closed = new List<WeakReference>();
            foreach (var item in Tabs.TabItems.OfType<TabViewItem>().Skip(1).ToArray())
            {
                closed.Add(new WeakReference(((NavigatorTabContent)item.Tag).Navigator));
                CloseTab(item);
            }
            await Sample("closed-to-one");
            // Diagnostic collection distinguishes retained pages from pending finalizers.
            // This is compiled out of production; no working-set trimming is performed.
            for (var pass = 0; pass < 6; pass++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                // WinRT reference-tracker cleanup also needs a UI dispatcher turn.
                await Task.Delay(300);
                if (closed.All(reference => !reference.IsAlive)) break;
            }
            await Sample("closed-after-diagnostic-gc");
            var closedPagesAlive = closed.Count(reference => reference.IsAlive);
            await VerifyTabStateAsync();
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Passed = true, Samples = samples, ClosedPagesAlive = closedPagesAlive,
                SelectionAndScrollPreserved = true, SidebarRestored = true,
                HomeAndPreviewAvailable = true, UnvisitedTabsDeferred = true,
                SwitchMilliseconds = switchMilliseconds
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = false, Error = error.ToString(), Samples = samples }));
        }

        async Task Ready()
        {
            for (var i = 0; i < 150; i++)
            {
                if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { IsLoaded: true } page } }
                    && !page.ViewModel.IsLoading)
                {
                    await Task.Delay(250);
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
            samples.Add(new { Stage = stage, Tabs = Tabs.TabItems.Count,
                TabCaptionGap = captionLeft - buttonRight,
                PrivateBytes = process.PrivateMemorySize64, WorkingSet = process.WorkingSet64,
                ManagedBytes = GC.GetTotalMemory(false), AllocatedBytes = GC.GetTotalAllocatedBytes(false),
                RealizedItems = Tabs.TabItems.OfType<TabViewItem>().Sum(item =>
                {
                    var page = ((NavigatorTabContent)item.Tag).Navigator;
                    if (page is null) return 0;
                    var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                    return surface.RealizedCount;
                }) });
        }
    }
}
#endif
