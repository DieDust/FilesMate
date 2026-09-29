#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Views;
using FilesMate.Core.Metadata;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunTagLayoutSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Tag checks require an isolated UI-test profile.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1920, 1240));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tag-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            var source = Directory.CreateDirectory(Path.Combine(fixture, "项目资料")).FullName;
            var names = new[] { "01 产品设计方案.md", "02 会议记录.txt", "03 旅行计划.pdf", "04 项目进度与交付清单.docx",
                "05 预算明细.xlsx", "06 使用说明.txt", "07 界面设计反馈.md", "08 九月工作总结.docx", "09 参考资料.pdf", "10 阅读笔记.md" }
                .Concat(Enumerable.Range(11, 80).Select(i => $"{i:00} 归档记录.txt")).ToArray();
            var paths = names.Select(n => Path.Combine(source, n)).ToArray();
            foreach (var path in paths) await File.WriteAllTextAsync(path, "FilesMate tag layout fixture");
            await using var store = new SqliteFileMetadataStore(Path.Combine(fixture, "tags.db"));
            var longTag = await store.CreateTagAsync("项目资料与设计评审待确认", "#5B8DEF");
            var work = await store.CreateTagAsync("工作", "#32A873");
            var pending = await store.CreateTagAsync("待整理", "#E9A23B");
            await store.SetTagsAsync(FileIdentity.FromNormalizedPath(paths[0]), [longTag.Id, work.Id, pending.Id]);
            await store.SetTagsAsync(FileIdentity.FromNormalizedPath(paths[1]), [work.Id]);
            await store.SetTagsAsync(FileIdentity.FromNormalizedPath(paths[2]), [work.Id, pending.Id]);
            await store.SetTagsAsync(FileIdentity.FromNormalizedPath(paths[3]), [longTag.Id]);
            await store.SetTagsAsync(FileIdentity.FromNormalizedPath(paths[6]), [pending.Id]);
            var tags = new Dictionary<string, IReadOnlyList<TagDefinition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths) tags[path] = await store.GetTagsAsync(FileIdentity.FromNormalizedPath(path));

            AddNavigatorTab(source);
            await Until(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading);
            var navigator = (NavigatorPage)TabHost.Content;
            var files = (FileDetailsSurface)navigator.FindName("FileSurface");
            files.SetLayout(FileLayoutKind.Details);
            files.SetColumns(DetailsColumn.Defaults());
            await Until(() => files.TrySelectByPath(paths[0]) && Row(files, names[0]) is not null);
            var row = Row(files, names[0])!;
            var originalNameWidth = ((TextBlock)row.FindName("NameText")).ActualWidth;
            BindTags(files);
            await Layout();
            Require(Math.Abs(((TextBlock)row.FindName("NameText")).ActualWidth - originalNameWidth) < .5,
                "Adding tags reduced the filename width.");
            var tagCell = (Grid)row.FindName("TagCell");
            var host = (StackPanel)row.FindName("TagHost");
            Require(tagCell.Visibility == Visibility.Visible && Grid.GetColumn(tagCell) != Grid.GetColumn((Grid)row.FindName("NameCell")),
                "Tags need a separate column.");
            Require(host.Children.OfType<TextBlock>().Any(t => t.Text == "+2"), "List overflow count is missing.");
            CheckHost(host, tagCell, tags[paths[0]]);
            Require(PolishDescendants(host).OfType<TextBlock>().Any(t => t.IsTextTrimmed), "Long tag must be ellipsized inside its column.");
            var sortRequests = 0;
            files.SortRequested += (_, _) => sortRequests++;
            var header = PolishDescendants((Grid)files.FindName("DetailsHeader")).OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == Localization.StringTable.Get("TagsTitle"));
            ((IInvokeProvider)new ButtonAutomationPeer(header).GetPattern(PatternInterface.Invoke)).Invoke();
            Require(sortRequests == 0, "Tag header unexpectedly sorted filenames.");
            var toolbar = (AdaptiveCommandToolbar)navigator.FindName("Commands");
            var sortMenu = (Flyout)((Button)toolbar.FindName("SortButton")).Flyout;
            Require(!PolishDescendants(sortMenu.Content).OfType<RadioButton>().Any(i => i.Content as string == Localization.StringTable.Get("TagsTitle")), "Unsupported tag sorting appeared in the toolbar.");
            report["IndependentNameAndTagColumns"] = true;

            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Layout();
                var chip = host.Children.OfType<Border>().First();
                var color = ((SolidColorBrush)chip.Background).Color;
                Require(theme == AppThemeKind.Dark ? color.A < 64 : color.A > 128,
                    $"Tag background did not follow the theme: {theme}, {color}.");
                await Capture(Content, "tag-details-" + theme + ".png");
            }
            foreach (var width in new[] { 64d, 320d })
            {
                files.SetColumns(files.GetColumns().Select(c => c.Id == DetailsColumnId.Tags ? c with { Width = width } : c).ToArray());
                await Layout();
                CheckHost(host, tagCell, tags[paths[0]]);
                Require(Math.Abs(((TextBlock)row.FindName("NameText")).ActualWidth - originalNameWidth) < .5, "Resizing tags changed filename width.");
            }
            var reordered = files.GetColumns().OrderBy(c => c.Id == DetailsColumnId.Tags ? 0 : 1).ToArray();
            files.SetColumns(reordered);
            await Layout();
            Require(Grid.GetColumn(tagCell) < Grid.GetColumn((Grid)row.FindName("NameCell")), "Tags could not move before the name.");
            files.SetColumns(reordered.Select(c => c.Id == DetailsColumnId.Tags ? c with { Visible = false } : c).ToArray());
            await Layout();
            Require(tagCell.Visibility == Visibility.Collapsed, "Hidden tag column still renders.");
            files.SetColumns(DetailsColumn.Defaults());
            report["ResizeReorderHide"] = true;

            files.SetLayout(FileLayoutKind.Grid);
            var metrics = new List<object>();
            foreach (var preset in GridSizePreset.All)
            {
                files.SetGridSize(preset);
                await Until(() => Tile(files, names[0]) is { IsLoaded: true } tile && Math.Abs(tile.ActualHeight - preset.ItemHeight) < 1);
                await Layout();
                var tile = Tile(files, names[0])!;
                var name = (TextBlock)tile.FindName("NameText");
                var gridTags = (StackPanel)tile.FindName("TagHost");
                var nameBounds = Bounds(name, tile);
                var tagBounds = Bounds(gridTags, tile);
                await Capture(files, $"tag-grid-{preset.Slot}-layout.png");
                Require(tagBounds.Top >= nameBounds.Bottom + 2 && tagBounds.Bottom <= tile.ActualHeight,
                    $"Tag overlaps the filename or next grid row at {preset.Slot}: {nameBounds}, {tagBounds}.");
                CheckHost(gridTags, tile, tags[paths[0]]);
                for (var y = tagBounds.Top + 1; y < tagBounds.Bottom; y += 2)
                    Require(GridSizePreset.IndexFromPoint(tagBounds.Left + tagBounds.Width / 2, y, 1, 1, preset) == 0,
                        "Visible tags contain a non-clickable band.");
                Require(gridTags.Children.OfType<TextBlock>().Any(t => t.Text == (preset.ShowsTagNames ? "+2" : "+1")), "Wrong grid overflow count.");
                Require(PolishDescendants(gridTags).OfType<TextBlock>().Any(t => t.Text == longTag.Name) == preset.ShowsTagNames,
                    "Small icons should use dots, large icons should show a label.");
                Require(double.IsNaN(name.Height) && Math.Abs(name.MaxHeight - preset.TextHeight) < .5 && name.MaxLines == 2,
                    $"Tags reduced the two-line name area at {preset.Slot}.");
                Require(tagBounds.Top - nameBounds.Bottom <= 5, "Short filename has an unnecessary blank line before its tags.");
                var untagged = Tile(files, names[4]);
                if (untagged is not null) Require(((StackPanel)untagged.FindName("TagHost")).Children.Count == 0, "Recycled tile retained old tags.");
                metrics.Add(new { preset.Slot, tile.ActualWidth, tile.ActualHeight, Name = nameBounds, Tags = tagBounds });
                if (preset == GridSizePreset.Large || preset == GridSizePreset.Huge || preset == GridSizePreset.Medium)
                {
                    foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
                    {
                        await App.AppearanceViewModel.SetThemeAsync(theme);
                        await Layout();
                        await Capture(Content, $"tag-grid-{preset.Slot}-{theme}.png");
                    }
                }
            }
            report["SevenGridSizes"] = metrics;
            var scroller = (ScrollViewer)files.FindName("Scroller");
            scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
            await Layout();
            Require(PolishDescendants(files).OfType<FileTile>().Where(t => t.EntryId >= 0 && t.IsLoaded)
                .All(t => ((StackPanel)t.FindName("TagHost")).Children.Count == 0), "Scrolled tiles retained recycled tags.");
            scroller.ChangeView(null, 0, null, true);
            await Layout();
            Require(((StackPanel)Tile(files, names[0])!.FindName("TagHost")).Children.Count == 2, "Scrolling back lost tags.");
            report["GridRecycling"] = true;
            files.SetGridSize(GridSizePreset.Large);
            AppWindow.Resize(new(1150, 1000));
            await Layout();
            await Capture(Content, "tag-grid-compact.png");
            foreach (var tile in PolishDescendants(files).OfType<FileTile>().Where(t => t.EntryId >= 0 && t.IsLoaded))
                Require(Bounds((StackPanel)tile.FindName("TagHost"), tile).Right <= tile.ActualWidth + 1, "Compact grid clips tags.");

            await App.SearchIndex.RebuildAsync(SearchIndexSettings.Sanitize([source], [], false, 2));
            OpenSearchPage(new SearchPageRequest("01 产品"));
            await Until(() => TabHost.Content is SearchResultsPage s && s.IsLoaded && !s.IsSearching && s.ResultRows.Any());
            var search = (SearchResultsPage)TabHost.Content;
            var results = (FileDetailsSurface)search.FindName("Results");
            BindTags(results);
            await Until(() => Row(results, names[0]) is not null);
            await Layout();
            var result = Row(results, names[0])!;
            CheckHost((StackPanel)result.FindName("TagHost"), (Grid)result.FindName("TagCell"), tags[paths[0]]);
            Require(results.GetColumns().Any(c => c.Id == DetailsColumnId.Tags && c.Visible), "Search lost the tag column.");
            await Capture(Content, "tag-search-compact.png");
            report["SearchAndCompactGrid"] = true;
            report["Fixture"] = fixture;
            report["Passed"] = true;

            void BindTags(FileDetailsSurface surface)
            {
                surface.ResolveTags = entry => surface.ResolvePath?.Invoke(entry) is { } path && tags.TryGetValue(path, out var found) ? found : [];
                surface.RefreshRealizedTags();
            }
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tag-layout-smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        static FileRow? Row(FileDetailsSurface surface, string name) => PolishDescendants(surface).OfType<FileRow>().FirstOrDefault(r => r.IsLoaded && r.Entry.Name == name);
        static FileTile? Tile(FileDetailsSurface surface, string name) => PolishDescendants(surface).OfType<FileTile>().FirstOrDefault(t => t.IsLoaded && t.Entry.Name == name);
        static Rect Bounds(FrameworkElement element, UIElement relative) => element.TransformToVisual(relative).TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Layout() => await Task.Delay(180);
        static async Task Until(Func<bool> predicate)
        {
            for (var i = 0; i < 200; i++) { if (predicate()) return; await Task.Delay(40); }
            throw new TimeoutException("Tag UI did not reach its expected state.");
        }
        static void CheckHost(StackPanel host, FrameworkElement container, IReadOnlyList<TagDefinition> expected)
        {
            Require(expected.All(t => AutomationProperties.GetName(host).Contains(t.Name, StringComparison.Ordinal)), "Full tag names missing from accessible text.");
            Require(expected.All(t => (ToolTipService.GetToolTip(host) as string)?.Contains(t.Name, StringComparison.Ordinal) == true), "Full tag names missing from tooltip.");
            Require(Bounds(host, container).Left >= -1 && Bounds(host, container).Right <= container.ActualWidth + 1, "Tag host escaped its container.");
            foreach (var child in host.Children.OfType<FrameworkElement>())
            {
                Require(Bounds(child, host).Right <= host.ActualWidth + 1, "Tag content escaped the host.");
                Require(Bounds(child, host).Top >= -1 && Bounds(child, host).Bottom <= host.ActualHeight + 1, "Tag text exceeds its row height.");
            }
        }
    }
}
#endif
