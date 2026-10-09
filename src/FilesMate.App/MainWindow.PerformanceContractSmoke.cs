#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using FilesMate.Platform.Windows.Sorting;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunPortableProfileSmokeAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "test-profile", "portable-profile-smoke.json");
        var report = new Dictionary<string, object>();
        try
        {
            await Wait(() => TabHost.Content is NavigatorPage page && page.IsLoaded
                && Navigation.HomeLocation.IsHome(page.ViewModel.AddressText), "portable Home");
            var profile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-profile")) + Path.DirectorySeparatorChar;
            Require(Program.IsUiTestBuild, "Portable probe requires a UI-test build.");
            var shortcutStore = typeof(App).GetProperty("ShortcutSettingsStore", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var shortcutsPath = (string)shortcutStore.GetType().GetField("_filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shortcutStore)!;
            Require(Path.GetFullPath(shortcutsPath).StartsWith(profile, StringComparison.OrdinalIgnoreCase), "Shortcuts use another profile.");
            Require(App.SearchIndex is not null && Path.GetFullPath(App.SearchIndex.FilePath).StartsWith(profile, StringComparison.OrdinalIgnoreCase), "Search index uses another profile.");
            var settings = Services.GlobalSearchService.LoadSettings();
            Require(!settings.Enabled && settings.Hotkey == "Ctrl+Shift+F10", "Resident settings were not read from the isolated seed.");
            Require(await Services.GlobalSearchService.StatusAsync() is null, "Test build contacted a resident host.");
            Require(!(await Services.GlobalSearchService.EnsureStartedAsync(show: true)).Ok, "Test build started a resident host.");
            Require(!(await Services.GlobalSearchService.ApplyAsync(settings with { Enabled = true })).Ok, "Test build applied resident settings.");
            await Services.GlobalSearchService.RestartForLanguageAsync();
            Require(File.Exists(Path.Combine(profile, "launch.log")), "Launch logging was not isolated.");
            App.LogFailure("PortableIsolationProbe", new InvalidOperationException("Controlled test-profile logging probe."));
            Require(File.Exists(Path.Combine(profile, "crash.log")), "Diagnostic logging was not isolated.");
            report["HomeLoaded"] = true;
            report["ShortcutsPath"] = shortcutsPath;
            report["SearchIndexPath"] = App.SearchIndex!.FilePath;
            report["ResidentHostOperationsDisabled"] = true;
            report["LaunchAndDiagnosticLogsIsolated"] = true;
            report["Passed"] = true;
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static async Task Wait(Func<bool> condition, string stage)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!condition())
            {
                if (deadline.IsCancellationRequested) throw new TimeoutException(stage);
                await Task.Delay(20);
            }
        }
    }

    private async Task RunPerformanceContractSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        var output = Path.Combine(AppContext.BaseDirectory, "performance-contract-smoke.json");
        var steps = Path.Combine(AppContext.BaseDirectory, "test-profile", "performance-contract-steps.jsonl");
        void Step(string stage) => File.AppendAllText(steps, JsonSerializer.Serialize(new { stage, utc = DateTime.UtcNow, thread = Environment.CurrentManagedThreadId }) + Environment.NewLine);
        try
        {
            Step("begin");
            Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, args) => Step("XamlUnhandled: " + args.Exception);
            var profile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-profile")) + Path.DirectorySeparatorChar;
            if (!Program.IsUiTestBuild || App.SearchIndex is null
                || !Path.GetFullPath(App.SearchIndex.FilePath).StartsWith(profile, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated test profile is required.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            await Wait(() => TabHost.Content is NavigatorPage { IsLoaded: true } page
                && page.ViewModel.Navigation.CurrentPath is { Length: > 0 } && !page.ViewModel.IsLoading, "initial navigator");
            var page = (NavigatorPage)TabHost.Content;
            var vm = page.ViewModel;
            var surface = (FileDetailsSurface)page.FindName("FileSurface");
            var restoring = (System.Collections.IDictionary)typeof(NavigatorPage)
                .GetField("_restoringViews", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            await Wait(() => !vm.IsLoading && !restoring.Contains(vm)
                && (Navigation.HomeLocation.IsHome(vm.AddressText)
                    || (vm.ViewIndex is { } initial && initial.Generation == vm.Navigation.CurrentGeneration
                        && initial.Sort == vm.Sort && surface.IsBoundTo(vm.Store, initial))), "initial view restoration");
            Step("initial-ready");
            var comparer = typeof(Navigation.PaneViewModel).GetField("_names", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm);
            Require(comparer is WindowsNameComparer, "Real navigator did not use WindowsNameComparer.");
            var fixture = Path.Combine(profile, "performance-contract-fixture");
            var child = Path.Combine(fixture, "child");
            Directory.CreateDirectory(child);
            for (var i = 0; i < 10_000; i++)
            {
                var path = Path.Combine(fixture, $"item-{i:D5}.bin");
                if (!File.Exists(path)) await File.WriteAllBytesAsync(path, []);
            }
            await UiAction(() => vm.Navigate(fixture));
            await Bound(10_001, string.Empty);
            Step("fixture-bound");
            for (var i = 0; i < 12; i++)
            {
                Step("cycle-" + i + "/rapid-filter");
                var previous = vm.ViewIndex;
                await UiAction(() => vm.SetFilterQuery("item-0"));
                await UiAction(() => vm.SetFilterQuery("item-09999"));
                await UiAction(() => vm.SetFilterQuery(string.Empty));
                await Bound(10_001, string.Empty, previous);
                Step("cycle-" + i + "/exact-filter");
                previous = vm.ViewIndex;
                await UiAction(() => vm.SetFilterQuery("item-09999"));
                await Bound(1, "item-09999", previous);
                Require(vm.Store![vm.ViewIndex![0]].Name == "item-09999.bin", "Latest query published the wrong row.");
                previous = vm.ViewIndex;
                await UiAction(() => vm.SetFilterQuery(string.Empty));
                await Bound(10_001, string.Empty, previous);
                Step("cycle-" + i + "/rapid-sort");
                previous = vm.ViewIndex;
                await UiAction(() => vm.RestoreSort(EntrySort.Name with { Ascending = false }));
                await UiAction(() => vm.RestoreSort(EntrySort.Size));
                await UiAction(() => vm.RestoreSort(EntrySort.Name));
                await Bound(10_001, string.Empty, previous);
                var names = vm.ViewIndex!.Select(row => vm.Store![row]).Where(entry => entry.Kind == EntryKind.File).Select(entry => entry.Name).ToArray();
                Require(names.Length == 10_000 && names[0] == "item-00000.bin" && names[^1] == "item-09999.bin", "Latest name sort did not win.");
                Step("cycle-" + i + "/rapid-navigation");
                await UiAction(() => vm.Navigate(child));
                await UiAction(() => vm.Navigate(fixture));
                await Bound(10_001, string.Empty);
                await UiAction(() => vm.Navigate(child));
                await Bound(0, string.Empty);
                Step("cycle-" + i + "/up");
                await UiAction(vm.Up);
                await Bound(10_001, string.Empty);
                Require(vm.AddressText == fixture, "Up did not return to the fixture.");
            }
            await UiAction(() => Require(surface.TrySelectByName("item-09999.bin"), "Selection failed after repeated rebinding."));
            Require(surface.SelectedPaths().Single() == Path.Combine(fixture, "item-09999.bin"), "Selection targeted the wrong row.");
            report["Passed"] = true;
            report["Cycles"] = 12;
            report["DirectoryEntries"] = 10_001;
            report["NativeWindowsNameComparer"] = true;
            report["RapidFilterClearSortNavigationUpAndSelection"] = true;
            report["DesktopInputUsed"] = false;
            report["Profile"] = profile;
            Step("passed");

            async Task Bound(int count, string query, EntryViewIndex? previous = null)
            {
                await Wait(() => !vm.IsLoading
                && vm.ViewIndex is { } index && index.Generation == vm.Navigation.CurrentGeneration
                && index.Filter.Query == query && index.Sort == vm.Sort && index.Count == count
                && !ReferenceEquals(previous, index) && surface.IsBoundTo(vm.Store, index), $"bound view: {count}, {query}");
                await UiAction(() => { });
            }
        }
        catch (Exception error)
        {
            report["Passed"] = false;
            report["Error"] = error.ToString();
            Step("failed: " + error);
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Task UiAction(Action action)
        {
            // User actions arrive in separate dispatcher callbacks. Even when all
            // fixture I/O and predicates complete synchronously, return to XAML
            // before invoking another action; never pump or nest its callbacks.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                try { action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
            })) completion.TrySetException(new InvalidOperationException("UI queue rejected the fixture action."));
            return completion.Task;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static async Task Wait(Func<bool> condition, string stage)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!condition())
            {
                if (deadline.IsCancellationRequested) throw new TimeoutException(stage);
                await Task.Delay(20);
            }
        }
    }
}
#endif
