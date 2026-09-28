#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.Omnibar;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunSearchPageSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            AppWindow.IsShownInSwitchers = false; AppWindow.Move(new(-10000, -10000)); AppWindow.Resize(new(1650, 1120));
            var fixture = Path.Combine(AppContext.BaseDirectory, "search-fixture"); Directory.CreateDirectory(fixture);
            var folder = Path.Combine(fixture, "项目资料"); Directory.CreateDirectory(folder);
            for (var i = 0; i < 450; i++) await File.WriteAllBytesAsync(Path.Combine(folder, $"年度报告-{i:D3}.pdf"), new byte[1000 + i * 20]);
            await File.WriteAllTextAsync(Path.Combine(folder, "预算说明.txt"), "fixture");
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Smoke test must use an isolated index.");
            await App.SearchIndex.RebuildAsync(SearchIndexSettings.Sanitize([fixture], [], false, 3));
            var sidebarLoads = 0;
            NavigationSidebar.Loaded += (_, _) => sidebarLoads++;
            AddNavigatorTab(folder);
            Omnibar? omni = null;
            for (var i = 0; i < 100; i++)
            {
                if (TabHost.Content is NavigatorPage { IsLoaded: true } navigator)
                    omni = FindDescendant<Omnibar>(navigator, _ => true);
                if (omni?.Text == folder) break;
                await Task.Delay(100);
            }
            if (omni?.Text != folder) throw new InvalidOperationException("Fixture folder did not finish navigating");
            var navPage = (NavigatorPage)TabHost.Content;
            await navPage.VerifyPaneCycleAsync(report, fixture);
            await Task.Delay(120);
            var newButton = FindDescendant<Button>(navPage, b => b.Name == "NewButton")!;
            var paneCard = FindDescendant<Border>(navPage, b => b.Name == "PaneCard")!;
            Require(Math.Abs(newButton.TransformToVisual(navPage).TransformPoint(new(0, 0)).X - paneCard.TransformToVisual(navPage).TransformPoint(new(0, 0)).X) < 1,
                "New command is not aligned with the file card");
            Require(newButton.Flyout.Placement == Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
                "New flyout is not anchored to the left edge");
            report["NewMenuAndToolbarAlignment"] = true;
            omni.BeginSearch();
            await Task.Delay(100);
            var inlineSearch = (TextBox)omni.FindName("SearchBox");
            var searchHost = (FrameworkElement)omni.FindName("SearchHost");
            var placeholder = (TextBlock)omni.FindName("SearchPlaceholderLabel");
            VerifyCentered(placeholder, searchHost);
            inlineSearch.Text = "年度报告";
            await Task.Delay(600);
            var inlineContent = FindDescendant<FrameworkElement>(inlineSearch, element => element.Name == "ContentElement")!;
            VerifyCentered(inlineContent, searchHost);
            report["SearchTextAndPlaceholderCentered"] = true;
            var inputArea = (FrameworkElement)omni.FindName("SearchInputArea");
            foreach (var y in new[] { 2d, inputArea.ActualHeight - 2 })
            {
                var point = inputArea.TransformToVisual((UIElement)Content).TransformPoint(new(inputArea.ActualWidth / 2, y));
                Require(Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, (UIElement)Content)
                    .Any(hit => ReferenceEquals(hit, inputArea)), "Search field padding is not clickable");
            }
            report["SearchFieldPaddingHitTestable"] = true;
            var savedCulture = System.Globalization.CultureInfo.CurrentUICulture;
            var savedTheme = App.AppearanceViewModel!.Current.Theme;
            var savedPlaceholder = placeholder.Text;
            try
            {
                foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
                {
                    await App.AppearanceViewModel.SetThemeAsync(theme);
                    foreach (var sample in new[] { ("zh-CN", "搜索此文件夹", "年度报告"), ("en-US", "Search this folder", "Annual report"), ("ja-JP", "このフォルダーを検索", "年次報告書") })
                    {
                        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(sample.Item1);
                        placeholder.Text = sample.Item2;
                        inlineSearch.Text = sample.Item3;
                        await Task.Delay(100);
                        VerifyCentered(placeholder, searchHost);
                        VerifyCentered(inlineContent, searchHost);
                    }
                }
                report["SearchCenteredInThreeLanguagesAndBothThemes"] = true;
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = savedCulture;
                placeholder.Text = savedPlaceholder;
                await App.AppearanceViewModel.SetThemeAsync(savedTheme);
                inlineSearch.Text = "年度报告";
                await Task.Delay(600);
            }
            await Capture((UIElement)Content, "search-page-omnibar-alignment.png");
            Click((Button)omni.FindName("MoreSearchButton"));
            await Task.Delay(150);
            if (TabHost.Content is not SearchResultsPage page) throw new InvalidOperationException("More did not open a search tab.");
            await Wait(page);
            var pageQuery = (TextBox)page.FindName("QueryBox");
            VerifyCentered(FindDescendant<FrameworkElement>(pageQuery, element => element.Name == "ContentElement")!, pageQuery);
            report["InitialRequest"] = page.Request;
            report["InitialResultCount"] = page.ResultCount;
            report["InitialStatus"] = ((TextBlock)page.FindName("StatusLabel")).Text;
            Require(page.Request.Query == "年度报告" && page.Request.Scope == folder && page.ResultCount == 200, "Query/scope or pagination lost");
            report["QuickSearchToPage"] = true;
            var list = (FileDetailsSurface)page.FindName("Results");
            var first = page.ResultRows.Select(r => r.Path).ToHashSet();
            var selectedPath = page.ResultRows[0].Path;
            Require(list.TrySelectByPath(selectedPath), "Initial selection failed");
            await Task.Delay(120);
            var scroller = (ScrollViewer)list.FindName("Scroller");
            scroller.ChangeView(null, scroller.ScrollableHeight - 30, null, true);
            await Task.Delay(100); await Wait(page);
            Require(page.ResultCount == 400 && page.ResultRows.Take(200).All(r => first.Contains(r.Path)), "Scroll did not append 200 results");
            Require(page.ResultRows.Select(r => r.Path).Distinct().Count() == 400, "Duplicate results while appending");
            Require(list.PrimaryPath() == selectedPath && scroller.VerticalOffset > 0, "Append lost selection or scroll position: " + list.PrimaryPath() + " / " + scroller.VerticalOffset);
            scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
            await Task.Delay(100); await Wait(page);
            Require(page.ResultCount == 450, "Final batch was not appended");
            Require(((TextBlock)page.FindName("StatusLabel")).Text.Contains("450"), "Total count missing from status");
            report["InfiniteScrollPreservesSelectionAndTotal"] = true;
            ((TextBox)page.FindName("SizeBox")).Text = ">8000";
            ((ComboBox)page.FindName("SortBox")).SelectedIndex = (int)SearchResultSort.SizeDescending;
            await Wait(page);
            Require(page.ResultCount == 99 && page.ResultRows[0].Hit.Size == 9980, "Metadata filter or sort failed");
            report["MetadataFilterAndSort"] = true;
            Require(((FrameworkElement)page.FindName("SearchHeader")).ActualHeight <= 80, "Search controls took over the results area");
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme); await Task.Delay(350);
                VerifyCentered(FindDescendant<FrameworkElement>(pageQuery, element => element.Name == "ContentElement")!, pageQuery);
                var searchButton = (Button)page.FindName("SearchButton");
                var searchInk = ((Microsoft.UI.Xaml.Media.SolidColorBrush)searchButton.Foreground).Color;
                report["SearchButtonInk" + theme] = searchInk.ToString();
                await VerifySearchInteractionsAsync(page, report);
                await Capture((UIElement)Content, "search-page-" + theme + ".png");
                await VerifyFilterPopupsAsync(page, report, theme.ToString());
                Require(searchInk.R == (theme == AppThemeKind.Dark ? 0 : 255), "Search button retained ink from the previous theme");
            }
            var resultHeight = list.ActualHeight;
            var filters = (Flyout)page.FindName("FilterFlyout");
            filters.ShowAt((Button)page.FindName("AdvancedButton"));
            await Task.Delay(250);
            Require(Math.Abs(list.ActualHeight - resultHeight) < 1, "Opening filters squeezed the result list");
            Click((Button)page.FindName("ApplyButton")); await Wait(page);
            Require(page.ResultCount == 99, "Applying flyout filters changed the search");
            report["FiltersOverlayResults"] = true;
            var dates = (ComboBox)page.FindName("DatePresetBox");
            ((Flyout)page.FindName("DateFlyout")).ShowAt((Button)page.FindName("DateButton"));
            await Task.Delay(120);
            Click((Button)((Grid)page.FindName("DatePresets")).Children[1]); await Wait(page);
            Require(page.Request.Modified == "today" && page.ResultCount == 99, "Clicking Today did not apply the date filter");
            dates.SelectedIndex = 0; await Wait(page);
            ((Flyout)page.FindName("TypeFlyout")).ShowAt((Button)page.FindName("TypeButton"));
            await Task.Delay(120);
            var formats = (Grid)page.FindName("FormatChoices");
            var pdf = (Microsoft.UI.Xaml.Controls.Primitives.ToggleButton)formats.Children[0];
            ((IToggleProvider)new ToggleButtonAutomationPeer(pdf).GetPattern(PatternInterface.Toggle)).Toggle();
            // Automation Toggle changes the checked state; invoke the same routed click for immediate filtering.
            page.ApplyFormatForSmoke(pdf);
            await Wait(page);
            Require(page.Request.Extensions == "pdf" && page.ResultCount == 99, "Format chip did not apply");
            ((Flyout)page.FindName("TypeFlyout")).Hide();
            ((Flyout)page.FindName("SizeFlyout")).ShowAt((Button)page.FindName("SizeButton"));
            await Task.Delay(120);
            var minimum = (NumberBox)page.FindName("MinimumSize"); var maximum = (NumberBox)page.FindName("MaximumSize");
            minimum.Value = 8001; maximum.Value = 9000; ((ComboBox)page.FindName("SizeUnit")).SelectedIndex = 0;
            Click((Button)page.FindName("ApplySizeButton")); await Wait(page);
            Require(page.ResultCount == 50 && page.ResultRows.All(r => r.Hit.Size is >= 8001 and <= 9000), "Numeric size range did not apply");
            maximum.Value = double.NaN; minimum.Value = 8001;
            Click((Button)page.FindName("ApplySizeButton")); await Wait(page);
            report["MouseDrivenFormatsDatesAndSizes"] = true;
            AppWindow.Resize(new(1080, 850)); await Task.Delay(300);
            report["CompactHeaderHeight"] = ((FrameworkElement)page.FindName("SearchHeader")).ActualHeight;
            report["NarrowResultViewportHeight"] = list.ActualHeight;
            report["NarrowPageHeight"] = page.ActualHeight;
            await Capture((UIElement)Content, "search-page-narrow.png");
            Require(list.ActualHeight > page.ActualHeight * 0.5, "Search results no longer occupy most of the short window");
            report["ActualRows"] = list.RealizedCount;
            Require((int)report["ActualRows"] < 99, "Search results are not virtualized");
            var saved = page.Request;
            CloseTab((TabViewItem)Tabs.SelectedItem); ReopenClosedTab();
            await Task.Delay(100); page = (SearchResultsPage)TabHost.Content; await Wait(page);
            Require(page.Request == saved, "Reopen lost search conditions");
            report["ReopenSearchTab"] = true;
            var searchProfile = Path.GetDirectoryName(Program.SettingsPath(Localization.LanguageSettings.DefaultFilePath))!;
            SearchSortConfiguration.Save(SearchResultSort.Name, searchProfile);
            await Task.Delay(180); await Wait(page);
            Require(page.Request.Sort == SearchResultSort.Name, "Visible search did not follow shared sorting");
            var searchTab = (TabViewItem)Tabs.SelectedItem;
            AddNavigatorTab(folder); await Task.Delay(80);
            SearchSortConfiguration.Save(SearchResultSort.NameDescending, searchProfile);
            Tabs.SelectedItem = searchTab; await Task.Delay(150); await Wait(page);
            Require(page.Request.Sort == SearchResultSort.NameDescending, "Inactive search did not refresh shared sorting when restored");
            report["SharedSortingUpdatesVisibleAndInactiveTabs"] = true;
            ((TextBox)page.FindName("QueryBox")).Text = "预算";
            ((TextBox)page.FindName("SizeBox")).Text = "";
            ((TextBox)page.FindName("ExtensionsBox")).Text = "";
            await page.SearchAsync(); await Wait(page);
            Require(page.ResultCount == 1, "New search reused stale results");
            report["ReplacePendingQuery"] = true;
            var transfer = CreateTabTransfer((TabViewItem)Tabs.SelectedItem);
            var parsed = Navigation.TabTransferPayload.Parse(transfer.Serialize());
            Require(parsed?.State.Left.Path == page.Request.Location, "Tab transfer lost the search");
            report["SearchTabTransfer"] = true;
            var actionSurface = (FileDetailsSurface)page.FindName("Results");
            var originalPath = page.ResultRows[0].Path;
            actionSurface.TrySelectByPath(originalPath);
            var renamed = await actionSurface.RenameRequested!(originalPath, "预算说明-改名.txt");
            Require(renamed is not null && File.Exists(renamed) && !File.Exists(originalPath), "Search inline rename did not update the real file");
            Require(page.ResultRows.Any(r => r.Path == renamed), "Search rename did not update its result path");
            var destination = Path.Combine(fixture, "drop-target", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(destination);
            await actionSurface.DropRequested!(new([renamed!], destination, Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy));
            Require(File.Exists(Path.Combine(destination, Path.GetFileName(renamed!))), "Search drop did not copy the file");
            File.Move(renamed!, originalPath, true);
            report["SearchRenameAndFileDrop"] = true;
            Require(sidebarLoads == 0, "Sidebar was recreated while navigating tabs");
            report["PersistentSidebarAcrossNavigation"] = true;
            await VerifySearchSettingsContrastAsync(report);

            foreach (var language in new[] { "zh-CN", "en-US", "ja-JP" })
            {
                var oldCulture = System.Globalization.CultureInfo.CurrentUICulture;
                var oldDefaultCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
                try
                {
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
                    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
                    OpenSearchPage(new("年度报告", folder, Size: ">8000"));
                    page = (SearchResultsPage)TabHost.Content; await Wait(page);
                    await Task.Delay(150);
                    var localizedQuery = (TextBox)page.FindName("QueryBox");
                    VerifyCentered(FindDescendant<FrameworkElement>(localizedQuery, element => element.Name == "ContentElement")!, localizedQuery);
                    await Capture((UIElement)Content, "search-page-" + language + "-narrow.png");
                    ((ComboBox)page.FindName("DatePresetBox")).SelectedIndex = 6;
                    await Task.Delay(350); await Wait(page);
                    await VerifyFilterPopupsAsync(page, report, language);
                    CloseTab((TabViewItem)Tabs.SelectedItem);
                }
                finally
                {
                    System.Globalization.CultureInfo.CurrentUICulture = oldCulture;
                    System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = oldDefaultCulture;
                }
            }
            report["LocalizedNarrowLayouts"] = true;

            AppWindow.Resize(new(1650, 1120));
            await App.AppearanceViewModel!.SetThemeAsync(AppThemeKind.Light);
            OpenSettings("tags");
            TagManagementPage? tags = null;
            for (var i = 0; i < 100; i++)
            {
                tags = FindDescendant<TagManagementPage>((DependencyObject)Content, _ => true);
                if (tags?.IsLoaded == true) break;
                await Task.Delay(100);
            }
            if (tags is null) throw new InvalidOperationException("Tag settings did not load");
            await Task.Delay(250);
            var tagRows = (StackPanel)tags.FindName("TagRows");
            if (tagRows.Children.Count > 0)
            {
                report["TagRowsHeight"] = tagRows.ActualHeight;
                var container = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(tagRows);
                Require(container.ActualHeight <= tagRows.ActualHeight + 1, "Tag card retained an empty row");
            }
            await Capture((UIElement)Content, "search-page-tags.png");
            CloseSettings();
            report["Passed"] = true;
        }
        catch (Exception e) { report["Passed"] = false; report["Error"] = e.ToString(); }
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "search-page-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        static void VerifyCentered(FrameworkElement text, FrameworkElement host)
        {
            Require(text is not null, "Search text content was not realized");
            var bounds = text!.TransformToVisual(host).TransformBounds(new(0, 0, text.ActualWidth, text.ActualHeight));
            Require(Math.Abs(bounds.Y + bounds.Height / 2 - host.ActualHeight / 2) <= 1.5,
                $"Search text is not centered: {text.Name} {bounds} inside {host.ActualHeight}");
        }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        static async Task Wait(SearchResultsPage page)
        {
            await Task.Delay(100);
            for (var i = 0; i < 200 && (!page.IsLoaded || page.IsSearching); i++) await Task.Delay(100);
            if (!page.IsLoaded || page.IsSearching) throw new TimeoutException("Search did not complete");
        }
    }
}
#endif
