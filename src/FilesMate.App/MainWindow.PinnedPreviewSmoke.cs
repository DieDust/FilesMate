#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Localization;
using FilesMate.App.Views;
using Microsoft.UI.Xaml.Controls;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunPinnedPreviewSmokeAsync()
    {
        var result = new Dictionary<string, object>();
        var fixture = Path.Combine(AppContext.BaseDirectory, "pinned-preview-fixture-" + Guid.NewGuid().ToString("N"));
        var otherFixture = fixture + "-other";
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));
            Directory.CreateDirectory(fixture);
            Directory.CreateDirectory(otherFixture);
            var first = Path.Combine(fixture, "reference.txt");
            var second = Path.Combine(fixture, "other.txt");
            File.WriteAllText(first, "first version");
            File.WriteAllText(second, "another file");
            File.WriteAllText(Path.Combine(otherFixture, "other.txt"), "another file in another folder");
            AddNavigatorTab(fixture);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            NavigatorPage? page = null;
            for (var i = 0; i < 160; i++)
            {
                if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { IsLoaded: true } candidate } }
                    && !candidate.ViewModel.IsLoading && candidate.ViewModel.ItemCount == 2)
                { page = candidate; break; }
                await Task.Delay(50);
            }
            if (page is null) throw new IOException("Preview fixture did not load.");
            var surface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", flags)!.GetValue(page)!;
            var show = typeof(NavigatorPage).GetMethod("SetPreviewVisible", flags)!;
            var toggle = typeof(NavigatorPage).GetMethod("TogglePinnedPreview", flags)!;
            string? LoadedPath() => (string?)typeof(NavigatorPage).GetField("_loadedPreviewPath", flags)!.GetValue(page);
            string? PinnedPath() => (string?)typeof(NavigatorPage).GetField("_pinnedPreviewPath", flags)!.GetValue(page);
            var pane = (PreviewPane)typeof(NavigatorPage).GetProperty("PreviewHost", flags)!.GetValue(page)!;
            string[] Lines() => (string[])typeof(PreviewPane).GetField("_textLines", flags)!.GetValue(pane)!;

            show.Invoke(page, [true]);
            if (!surface.TrySelectByName("reference.txt")) throw new IOException("Cannot select reference file.");
            await Until(() => LoadedPath() == first && Lines().Any(line => line.Contains("first version")));
            toggle.Invoke(page, null);
            if (PinnedPath() != first) throw new IOException("Preview did not pin selected file.");
            show.Invoke(page, [false]);
            if (PinnedPath() != first || _pinnedPreviewPath != first)
                throw new IOException("Closing the pane discarded its pin.");
            if (Lines().Length != 0 || typeof(NavigatorPage).GetField("_pinnedPreviewWatcher", flags)!.GetValue(page) is not null)
                throw new IOException("Closing the pane retained preview resources.");
            result["CloseKeepsPinAndReleasesResources"] = true;
            show.Invoke(page, [true]);
            await Until(() => LoadedPath() == first && Lines().Any(line => line.Contains("first version")));
            result["ReopenRestoresPinnedPreview"] = true;

            var sourceTab = (TabViewItem)Tabs.SelectedItem;
            AddNavigatorTab(otherFixture);
            NavigatorPage? secondPage = null;
            for (var i = 0; i < 160; i++)
            {
                if (Tabs.SelectedItem is TabViewItem { Tag: NavigatorTabContent { Navigator: { IsLoaded: true } candidate } }
                    && !candidate.ViewModel.IsLoading && candidate.ViewModel.ItemCount == 1)
                { secondPage = candidate; break; }
                await Task.Delay(50);
            }
            if (secondPage is null) throw new IOException("Second tab did not load.");
            var secondTab = (TabViewItem)Tabs.SelectedItem;
            var secondPane = (PreviewPane)typeof(NavigatorPage).GetProperty("PreviewHost", flags)!.GetValue(secondPage)!;
            var secondSurface = (FileDetailsSurface)typeof(NavigatorPage).GetField("FileSurface", flags)!.GetValue(secondPage)!;
            string? SecondPinnedPath() => (string?)typeof(NavigatorPage).GetField("_pinnedPreviewPath", flags)!.GetValue(secondPage);
            string[] SecondLines() => (string[])typeof(PreviewPane).GetField("_textLines", flags)!.GetValue(secondPane)!;
            await Until(() => SecondPinnedPath() == first && SecondLines().Any(line => line.Contains("first version")));
            if (!secondSurface.TrySelectByName("other.txt")) throw new IOException("Cannot select in second tab.");
            await Task.Delay(250);
            if (!SecondLines().Any(line => line.Contains("first version")))
                throw new IOException("Second tab did not retain the pinned preview.");
            result["PinnedPreviewAppearsAcrossTabs"] = true;
            show.Invoke(secondPage, [false]);
            if (SecondPinnedPath() != first || _pinnedPreviewVisible)
                throw new IOException("Closing the pane in another tab lost the pin or visibility state.");
            Tabs.SelectedItem = sourceTab;
            await Until(() => page.IsLoaded);
            if ((bool)typeof(NavigatorPage).GetField("_previewVisible", flags)!.GetValue(page)! || PinnedPath() != first)
                throw new IOException("Switching tabs reopened a hidden pinned preview.");
            result["HiddenPinStaysHiddenAcrossTabs"] = true;
            show.Invoke(page, [true]);
            await Until(() => LoadedPath() == first && Lines().Any(line => line.Contains("first version")));

            if (!surface.TrySelectByName("other.txt")) throw new IOException("Cannot select other file.");
            await Task.Delay(250);
            if (LoadedPath() != first || !Lines().Any(line => line.Contains("first version")))
                throw new IOException("Pinned preview followed a different selection.");
            result["SelectionDoesNotReplacePinnedPreview"] = true;

            File.WriteAllText(first, "updated version");
            await Until(() => Lines().Any(line => line.Contains("updated version")));
            result["ChangedFileReloads"] = true;

            File.Delete(first);
            var empty = (TextBlock)typeof(PreviewPane).GetField("EmptyText", flags)!.GetValue(pane)!;
            await Until(() => empty.Text == StringTable.Get("Preview_PinnedUnavailable"));
            result["RemovedFileDoesNotShowStalePreview"] = true;

            toggle.Invoke(page, null);
            await Until(() => LoadedPath() == second && Lines().Any(line => line.Contains("another file")));
            if (PinnedPath() is not null) throw new IOException("Unpin left the old source active.");
            result["UnpinResumesSelection"] = true;

            Tabs.SelectedItem = secondTab;
            await Until(() => secondPage.IsLoaded && SecondPinnedPath() is null);
            show.Invoke(secondPage, [true]);
            if (!secondSurface.TrySelectByName("other.txt")) throw new IOException("Cannot reselect in second tab.");
            await Until(() => SecondLines().Any(line => line.Contains("another file")));
            result["UnpinReachesOtherTabs"] = true;
            Tabs.SelectedItem = sourceTab;
            await Until(() => page.IsLoaded);

            show.Invoke(page, [false]);
            if (typeof(NavigatorPage).GetField("_pinnedPreviewWatcher", flags)!.GetValue(page) is not null)
                throw new IOException("Preview watcher survived closing the pane.");
            result["ClosingReleasesWatcher"] = true;
            result["Passed"] = true;

            static async Task Until(Func<bool> ready)
            {
                for (var i = 0; i < 120; i++)
                {
                    if (ready()) return;
                    await Task.Delay(50);
                }
                throw new TimeoutException("Pinned preview did not reach the expected state.");
            }
        }
        catch (Exception error) { result["Passed"] = false; result["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pinned-preview-smoke.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Close();
        try { Directory.Delete(fixture, recursive: true); } catch (IOException) { }
        try { Directory.Delete(otherFixture, recursive: true); } catch (IOException) { }
    }
}
#endif
