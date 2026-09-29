#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Core.Entries;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunIntegrationReviewSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        QuickPreviewWindow? active = null;
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An isolated test profile is required.");
            var preferencesPath = Program.SettingsPath(Services.ExplorerPreferencesService.DefaultFilePath);
            if (!preferencesPath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Preferences must belong to the isolated profile.");
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowAlternatingRows = true, DefaultSortAscending = true });
            Task latestSave;
            var earlierFailed = false;
            using (var reader = File.Open(preferencesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var earlierSave = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowAlternatingRows = false });
                latestSave = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { DefaultSortAscending = false });
                try { await earlierSave.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { earlierFailed = true; }
            }
            await latestSave;
            var savedPreferences = new Services.ExplorerPreferencesService(preferencesPath).Load();
            report["OlderBrowsingFailureCannotRevertLatest"] = new
            {
                Passed = earlierFailed && !App.ExplorerPreferences.ShowAlternatingRows && !App.ExplorerPreferences.DefaultSortAscending
                    && !savedPreferences.ShowAlternatingRows && !savedPreferences.DefaultSortAscending,
                MemoryStripe = App.ExplorerPreferences.ShowAlternatingRows, SavedStripe = savedPreferences.ShowAlternatingRows
            };
            await App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { MixChineseAndLatin = true });
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "review-fixture")).FullName;
            var names = new[] { "中国.txt", "Beta.txt", "阿.txt" };
            foreach (var name in names) File.WriteAllText(Path.Combine(fixture, name), name);
            OpenSearchPage(new SearchPageRequest("review-fixture", fixture));
            await Wait(() => TabHost.Content is SearchResultsPage { IsLoaded: true, IsSearching: false }, "search page");
            var page = (SearchResultsPage)TabHost.Content;
            var surface = (FileDetailsSurface)page.FindName("Results");
            var toolbar = (AdaptiveCommandToolbar)page.FindName("Commands");
            var query = (TextBox)page.FindName("QueryBox");
            var rows = names.Select((name, i) => new SearchResultRow(new AdvancedSearchHit(name, Path.Combine(fixture, name), false,
                Size: (i + 1) * 100, ModifiedUtcTicks: DateTime.UtcNow.AddMinutes(i).Ticks), i)).ToArray();
            void Bind(SearchResultRow[] values, bool append = false) => typeof(SearchResultsPage).GetMethod("BindRows", flags)!.Invoke(page, [values, append]);
            void SetSort(EntrySort sort) => typeof(SearchResultsPage).GetMethod("SetResultSort", flags)!.Invoke(page, [sort]);
            EntryItemsSource Items() => (EntryItemsSource)typeof(FileDetailsSurface).GetField("_items", flags)!.GetValue(surface)!;
            EntrySort CurrentSort() => (EntrySort)typeof(SearchResultsPage).GetMethod("SurfaceSort", flags)!.Invoke(page, null)!;
            Task Sorted() => Wait(() => typeof(SearchResultsPage).GetField("_resultSortBuild", flags)!.GetValue(page) is null, "sort build");
            string[] Order() => Items().Index!.Select(i => Items().Store![i].Name).ToArray();
            void Check(string key, bool ok, object? evidence = null) => report[key] = new { Passed = ok, Evidence = evidence };
            Bind(rows);
            surface.TrySelectByName("Beta.txt");
            SetSort(EntrySort.Name with { Grouping = EntryGrouping.Mixed });
            await Sorted();
            Check("NameSortUsesPinyin", Order().SequenceEqual(new[] { "阿.txt", "Beta.txt", "中国.txt" }), Order());
            Check("NameSortRetainsAlphabetIndex", Items().Index!.Sort.Column == EntrySortColumn.Name && Items().Index!.NameSections.Count == 3);
            Check("SortingPreservesSelection", surface.PrimaryPath() == rows[1].Path);

            // This is the provider's current order before a local sort was requested.
            typeof(SearchResultsPage).GetField("_resultSort", flags)!.SetValue(page, null);
            typeof(SearchResultsPage).GetProperty("Request")!.SetValue(page, page.Request with { Sort = SearchResultSort.ModifiedDescending });
            Bind(rows);
            surface.TrySelectByName("Beta.txt");
            typeof(AdaptiveCommandToolbar).GetMethod("ChangeGrouping", flags)!.Invoke(toolbar, [EntryGrouping.Mixed]);
            await Sorted();
            Check("GroupingPreservesDateOrder", CurrentSort().Column == EntrySortColumn.Modified && !CurrentSort().Ascending,
                new { Sort = CurrentSort(), Names = Order() });
            SetSort(EntrySort.Size with { Grouping = EntryGrouping.Mixed, Ascending = false });
            SetSort(EntrySort.Name with { Grouping = EntryGrouping.Mixed });
            await Sorted();
            Check("LatestSortWins", Order().SequenceEqual(new[] { "阿.txt", "Beta.txt", "中国.txt" }));
            var extra = new SearchResultRow(new AdvancedSearchHit("Delta.txt", Path.Combine(fixture, "Delta.txt"), false, Size: 400), 3);
            File.WriteAllText(extra.Path, "Delta");
            Bind([.. rows, extra], true);
            await Sorted();
            Check("AppendResortsWithoutLosingSelection", Order().SequenceEqual(new[] { "阿.txt", "Beta.txt", "Delta.txt", "中国.txt" }) && surface.PrimaryPath() == rows[1].Path, Order());

            QuickPreviewWindow? Card() => (QuickPreviewWindow?)typeof(SearchResultsPage).GetField("_quickPreview", flags)!.GetValue(page);
            var toggle = (EventHandler)typeof(FileDetailsSurface).GetField("QuickPreviewRequested", flags)!.GetValue(surface)!;
            async Task<QuickPreviewWindow> Open()
            {
                Activate(); Foreground(NativeHandle); surface.TrySelectByName("Beta.txt"); surface.Focus(FocusState.Programmatic);
                await Task.Delay(150); toggle(surface, EventArgs.Empty);
                // Grant foreground before waiting for Loaded: a background automation
                // window can otherwise legitimately trigger the outside-focus rule.
                active = Card() ?? throw new InvalidOperationException("Preview was not created.");
                Foreground(WinRT.Interop.WindowNative.GetWindowHandle(active));
                await Wait(() => Card() is { Content: FrameworkElement { IsLoaded: true } }, "preview");
                active = Card()!; Foreground(WinRT.Interop.WindowNative.GetWindowHandle(active));
                await active.LoadAsync(rows[1].Path); await Task.Delay(220); return active;
            }
            async Task Cleanup()
            {
                Card()?.Dismiss(); await Task.Delay(350); active = null;
            }
            var card = await Open();
            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Dark);
            await Task.Delay(180);
            Check("PreviewFollowsOwnerTheme", ((FrameworkElement)card.Content).ActualTheme == ElementTheme.Dark);
            await App.AppearanceViewModel.SetThemeAsync(AppThemeKind.Light);
            surface.TrySelectByName("阿.txt");
            await Wait(() => card.Title == "阿.txt", "selection follows");
            Check("SelectionReusesPreview", ReferenceEquals(card, Card()));
            surface.Selection.Clear();
            ((EventHandler)typeof(FileDetailsSurface).GetField("SelectionChanged", flags)!.GetValue(surface)!)(surface, EventArgs.Empty);
            await Task.Delay(500);
            Check("ClearingSearchSelectionDismisses", Card() is null && !card.RestoreOwnerFocus);
            await Cleanup();
            card = await Open();
            Activate(); Foreground(NativeHandle); query.Focus(FocusState.Keyboard);
            await Task.Delay(600);
            Check("SearchInputDismissesWithoutStealingFocus", Card() is null && !card.RestoreOwnerFocus
                && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), query));
            await Cleanup();
            card = await Open();
            surface.RestoreSelectedNames(["Beta.txt", "阿.txt"]);
            await Task.Delay(500);
            Check("MultipleSelectionDismisses", Card() is null && !card.RestoreOwnerFocus);
            await Cleanup();
            report["Passed"] = report.Values.All(value => (bool)value.GetType().GetProperty("Passed")!.GetValue(value)!);
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        finally { active?.Dismiss(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "integration-review-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static async Task Wait(Func<bool> condition, string name)
        { for (var i = 0; i < 180; i++) { if (condition()) return; await Task.Delay(50); } throw new TimeoutException(name); }
        static void Foreground(nint window)
        {
            var current = BrowsingGetCurrentThreadId();
            var foreground = BrowsingGetWindowThreadProcessId(BrowsingGetForegroundWindow(), out _);
            var attached = current != foreground && BrowsingAttachThreadInput(current, foreground, true);
            try { BrowsingSetForegroundWindow(window); }
            finally { if (attached) BrowsingAttachThreadInput(current, foreground, false); }
        }
    }
}
#endif
