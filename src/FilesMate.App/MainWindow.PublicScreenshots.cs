#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Search;
using Microsoft.UI.Xaml;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task CapturePublicScreenshotsAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            var fixture = Environment.GetEnvironmentVariable("FILESMATE_SCREENSHOT_FIXTURE")
                ?? throw new InvalidOperationException("A demonstration directory is required.");
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Screenshots require an isolated test profile.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1920, 1240));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            await App.AppearanceViewModel.SetAccentAsync(AccentKind.Default);
            App.SetFavoritesBarEnabled(true);
            await App.Favorites.AddAsync(new[] { "Documents", "Media library", "Website", "Delivery" }
                .Select(name => (Path.Combine(fixture, name), true)));
            AddNavigatorTab(fixture);
            for (var i = 0; i < 100 && (TabHost.Content is not NavigatorPage { IsLoaded: true } p || p.ViewModel.IsLoading); i++)
                await Task.Delay(100);
            var page = TabHost.Content as NavigatorPage ?? throw new InvalidOperationException("Demonstration folder did not load.");
            var single = page.CaptureClosedTab();
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Shot("workspace-" + theme.ToString().ToLowerInvariant());
            }
            var right = single.Left with { Path = Path.Combine(fixture, "Media library"), View = single.Left.View with { Details = false, GridSlot = 2 }, SelectedNames = [] };
            await page.RestoreClosedTabAsync(single with { Right = right });
            await Shot("dual-pane");
            await page.RestoreClosedTabAsync(single with { Right = right, Third = single.Left with { Path = Path.Combine(fixture, "Documents"), SelectedNames = [] }, PaneArrangement = PaneArrangement.LeftFocus });
            await Shot("three-pane");
            await page.RestoreClosedTabAsync(single);
            await Navigate(right.Path);
            await page.RestoreClosedTabAsync(single with { Left = right });
            await Task.Delay(1200);
            var media = page.CaptureClosedTab();
            if (media.Right is not null || media.Left.Path != right.Path || media.Left.View.Details)
                throw new InvalidOperationException("Media screenshot did not reach the requested single-pane icon view.");
            await Shot("media-thumbnails");
            await Navigate(single.Left.Path);
            await page.RestoreClosedTabAsync(single with { Left = single.Left with { SelectedNames = ["Project settings.json"] }, PreviewVisible = true });
            await Task.Delay(900);
            await Shot("document-preview");
            await page.RestoreClosedTabAsync(single);
            OpenSettings("appearance");
            await Task.Delay(500);
            await Shot("appearance");
            CloseSettings();
            await App.SearchIndex.RebuildAsync(SearchIndexSettings.Sanitize([fixture], [], false, 4));
            OpenSearchPage(new SearchPageRequest("Project"));
            await Task.Delay(1200);
            await Shot("search-page");
            report["Passed"] = true;
            report["Screenshots"] = new[] { "workspace-light", "workspace-dark", "dual-pane", "three-pane", "media-thumbnails", "document-preview", "appearance", "search-page" };

            async Task Navigate(string path)
            {
                page.ViewModel.Navigate(path);
                for (var i = 0; i < 100; i++)
                {
                    await Task.Delay(100);
                    if (!page.ViewModel.IsLoading && string.Equals(page.ViewModel.AddressText, path, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                throw new InvalidOperationException("Demonstration folder did not finish loading: " + path);
            }
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "public-screenshots.json"), JsonSerializer.Serialize(report));

        async Task Shot(string name)
        {
            await Task.Delay(500);
            await Capture((UIElement)Content, "public-" + name + ".png");
        }
    }
}
#endif
