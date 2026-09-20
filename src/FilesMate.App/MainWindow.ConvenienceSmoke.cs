#if FILESMATE_UI_TEST
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunConvenienceSmokeAsync()
    {
        var results = new Dictionary<string, object>();
        var output = Path.Combine(AppContext.BaseDirectory, "convenience-smoke.json");
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            var firstPage = await Ready();
            await firstPage.RunConvenienceChecksAsync(Check);
            await Check("BackupLocationOpensInFilesMateTab", async () =>
            {
                var previous = (TabViewItem)Tabs.SelectedItem;
                var fixture = Path.Combine(AppContext.BaseDirectory, "backup-open-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fixture);
                var source = Path.Combine(fixture, "source.txt"); var target = Path.Combine(fixture, "target.txt");
                File.WriteAllText(source, "new"); File.WriteAllText(target, "old");
                var transfer = await Platform.Windows.Operations.WindowsFileTransfer.RunAsync(new Platform.Windows.Operations.WindowsLocalFileOperations(),
                    [new(source, target)], false, (_, _) => Task.FromResult(new Core.Operations.FileConflictChoice(Core.Operations.FileConflictAction.Replace)));
                Require(transfer.Undo is not null, "replacement fixture failed");
                App.FileUndo.Push(transfer.Undo!);
                var expected = Path.GetDirectoryName(transfer.Undo!.Replacements.Single().Backup)!;
                var before = Tabs.TabItems.Count;
                var pending = BackupHistoryDialog.ShowAsync(firstPage);
                Button? location = null;
                for (var attempt = 0; attempt < 100 && location is null; attempt++)
                {
                    await Task.Delay(40);
                    location = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Content.XamlRoot)
                        .SelectMany(p => BackupDescendants(p.Child)).OfType<Button>()
                        .FirstOrDefault(b => ToolTipService.GetToolTip(b) as string == expected);
                }
                Require(location is not null, "backup location button missing");
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(location!)
                    .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await pending;
                var opened = await Ready();
                Require(Tabs.TabItems.Count == before + 1 && opened.ViewModel.AddressText == expected, "backup opened outside FilesMate or wrong path");
                CloseTab((TabViewItem)Tabs.SelectedItem); Tabs.SelectedItem = previous;
                App.FileUndo.Clear();
                Directory.Delete(fixture, recursive: true);
            });
            var path = firstPage.ViewModel.AddressText;
            var first = (TabViewItem)Tabs.SelectedItem;
            await Check("ClosedTabRestore", async () =>
            {
                AddNavigatorTab(path); var page = await Ready();
                Require(Surface(page).TrySelectByName("file12.txt"), "pre-close selection missing");
                Surface(page).RestoreScrollOffset(160); await Task.Delay(150);
                var offset = Surface(page).ScrollOffset;
                results["ClosedBefore"] = page.CaptureClosedTab();
                CloseTab((TabViewItem)Tabs.SelectedItem); ReopenClosedTab();
                var restored = await Ready(); await Task.Delay(700);
                results["ClosedAfter"] = restored.CaptureClosedTab();
                Require(Surface(restored).SelectedPaths().Any(p => p.EndsWith("file12.txt")), "reopen lost selection");
                Require(Math.Abs(Surface(restored).ScrollOffset - offset) < 2, "reopen lost scroll position");
            });
            await Check("TabDragDwellAndCancel", async () =>
            {
                QueueFileTabHover(first); await Task.Delay(150); CancelFileTabHover(); await Task.Delay(800);
                Require(!ReferenceEquals(Tabs.SelectedItem, first), "leave did not cancel tab dwell");
                QueueFileTabHover(first); await Task.Delay(900);
                Require(ReferenceEquals(Tabs.SelectedItem, first), "tab dwell did not activate target");
            });
            await Check("HibernateProtectAndRestore", async () =>
            {
                Tabs.SelectedItem = first; var page = await Ready();
                page.ViewModel.RestoreSort(EntrySort.Name with { Ascending = false });
                await Task.Delay(200);
                Surface(page).TrySelectByName("file12.txt"); Surface(page).RestoreScrollOffset(120);
                await Task.Delay(200); var before = page.CaptureClosedTab();
                Require(!await HibernateTabAsync(first), "active tab hibernated");
                AddNavigatorTab(path); await Ready();
                var state = (NavigatorTabContent)first.Tag;
                state.KeepAlive = true; Require(!await HibernateTabAsync(first), "protected tab hibernated"); state.KeepAlive = false;
                using (FileOperationLifetime.Begin()) Require(!await HibernateTabAsync(first), "file work was not protected");
                Require(await HibernateTabAsync(first), "idle tab did not hibernate");
                Require(((NavigatorTabContent)first.Tag).Navigator is null, "sleep retained navigator");
                Tabs.SelectedItem = first; var awake = await Ready(); await Task.Delay(700);
                Require(!ReferenceEquals(awake, page) && awake.ViewModel.Sort == before.Left.View.Sort, "tab was not rebuilt with original sort");
                Require(Surface(awake).SelectedPaths().Any(p => p.EndsWith("file12.txt")), "sleep lost selection");
                Require(Math.Abs(Surface(awake).ScrollOffset - before.Left.ScrollOffset) < 2, "sleep lost scroll");
            });
            await Check("IdlePolicyAndMemory", async () =>
            {
                while (Tabs.TabItems.Count < 8) { AddNavigatorTab(path); await Ready(); }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(300);
                var before = Sample();
                var weak = new List<WeakReference>();
                foreach (var tab in Tabs.TabItems.OfType<TabViewItem>().Where(t => !ReferenceEquals(t, Tabs.SelectedItem)))
                {
                    var state = (NavigatorTabContent)tab.Tag;
                    weak.Add(new WeakReference(state.Navigator));
                    state.InactiveSince = Environment.TickCount64 - (long)TimeSpan.FromMinutes(40).TotalMilliseconds;
                }
                var setting = App.ExplorerPreferences;
                await App.SetExplorerPreferencesAsync(setting with { TabMemory = TabMemoryMode.Off });
                await HibernateIdleTabsAsync();
                Require(Tabs.TabItems.OfType<TabViewItem>().All(t => ((NavigatorTabContent)t.Tag).Navigator is not null), "off mode discarded tabs");
                await App.SetExplorerPreferencesAsync(setting with { TabMemory = TabMemoryMode.Balanced });
                await HibernateIdleTabsAsync(); await Task.Delay(1000);
                var after = Sample();
                // Diagnostic-only GC verifies retained references; production never forces GC.
                for (int pass = 0; pass < 5; pass++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(300);
                    if (weak.All(w => !w.IsAlive)) break;
                }
                results["MemorySamples"] = new { Before = before, After = after, AfterDiagnosticGc = Sample(), ClosedPagesAlive = weak.Count(w => w.IsAlive) };
                Require(Tabs.TabItems.OfType<TabViewItem>().Count(t => ((NavigatorTabContent)t.Tag).Navigator is not null) == 1, "background pages retained");
                Require(weak.Count(w => w.IsAlive) <= 1, "hibernated page references retained");
                await App.SetExplorerPreferencesAsync(setting);
            });
        }
        catch (Exception error) { results["Fatal"] = error.ToString(); }
        finally
        {
            Save();
            App.FileUndo.Clear();
            Application.Current.Exit();
        }

        async Task Check(string name, Func<Task> run)
        {
            results["Running"] = name; Save();
            try { await run().WaitAsync(TimeSpan.FromSeconds(60)); results[name] = "Passed"; }
            catch (TimeoutException error)
            {
                results[name] = error.ToString();
                results["Fatal"] = "UI check timed out; the isolated test process was terminated.";
                Save();
                // A modal conflict can keep FileOperationLifetime busy indefinitely. This
                // compiled test harness must not leave an unreachable window running.
                Environment.Exit(1);
            }
            catch (Exception error) { results[name] = error.ToString(); }
            Save();
        }
        void Save() => File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        static IEnumerable<DependencyObject> BackupDescendants(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
                foreach (var child in BackupDescendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index))) yield return child;
        }
        async Task<NavigatorPage> Ready()
        {
            for (int i = 0; i < 200; i++)
            {
                if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { RestoreState: null, Navigator: { IsLoaded: true } page } }
                    && !page.ViewModel.IsLoading && page.ViewModel.ItemCount > 0) { await Task.Delay(250); return page; }
                await Task.Delay(50);
            }
            throw new TimeoutException("Navigator not ready");
        }
        static FileDetailsSurface Surface(NavigatorPage page) => (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        static object Sample() { using var p = Process.GetCurrentProcess(); return new { PrivateBytes = p.PrivateMemorySize64, WorkingSet = p.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false) }; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
