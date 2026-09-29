#if FILESMATE_UI_TEST
using System.Reflection;
using System.Text.Json;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Toolbar;
using FilesMate.App.Models;
using FilesMate.App.Views;
using FilesMate.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunShelfSmokeAsync()
    {
        var report = new Dictionary<string, object>();
        try
        {
            if (App.SearchIndex is null || !App.SearchIndex.FilePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Shelf checks require an isolated UI-test profile.");
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            AppWindow.Resize(new(1920, 1240));
            await App.AppearanceViewModel!.SetBackdropAsync(BackdropKind.Solid);
            var fixture = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "shelf-fixture-" + Guid.NewGuid().ToString("N"))).FullName;
            var source = Directory.CreateDirectory(Path.Combine(fixture, "Project files")).FullName;
            var other = Directory.CreateDirectory(Path.Combine(fixture, "Research")).FullName;
            var copyTo = Directory.CreateDirectory(Path.Combine(fixture, "Copies")).FullName;
            var moveTo = Directory.CreateDirectory(Path.Combine(fixture, "Delivery")).FullName;
            var paths = new[] { "Project brief.txt", "Release notes.md", "Design plan.json" }.Select(name => Path.Combine(source, name)).ToArray();
            var searchPath = Path.Combine(other, "Project research.txt");
            var dropPath = Path.Combine(other, "Project checklist.txt");
            foreach (var path in paths.Concat([searchPath, dropPath])) await File.WriteAllTextAsync(path, "FilesMate shelf demonstration");
            await App.FileShelf.RemoveAsync(await App.FileShelf.GetAsync());
            AddNavigatorTab(source);
            await Until(() => TabHost.Content is NavigatorPage p && p.IsLoaded && !p.ViewModel.IsLoading);
            var navigator = (NavigatorPage)TabHost.Content;
            var files = (FileDetailsSurface)navigator.FindName("FileSurface");
            var toolbar = (AdaptiveCommandToolbar)navigator.FindName("Commands");
            await Until(() => files.TrySelectByPath(paths[0]));
            await Click((Button)toolbar.ShelfAnchor);
            await Until(() => ((ContentControl)navigator.FindName("ShelfContent")).Content is FileShelfPanel { IsLoaded: true });
            var panel = (FileShelfPanel)((ContentControl)navigator.FindName("ShelfContent")).Content;
            var list = (ListView)panel.FindName("PathsList");
            await Until(() => ((Button)panel.FindName("EmptyAddSelectionButton")).IsEnabled);
            Require(((Grid)panel.FindName("SelectionRow")).Visibility == Visibility.Collapsed &&
                ((Grid)panel.FindName("ActionsRow")).Visibility == Visibility.Collapsed, "Empty shelves must hide irrelevant controls.");
            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                await Task.Delay(160);
                await Capture((Border)navigator.FindName("ShelfCard"), "shelf-empty-" + theme + ".png");
            }
            await Click((Button)panel.FindName("EmptyAddSelectionButton"));
            await Until(() => list.Items.Count == 1 && !Busy(panel));
            Require(list.SelectedItems.Count == 1, "Newly collected files must be selected.");
            await panel.AddPathsAsync(paths.Skip(1).ToArray());
            await panel.AddPathsAsync(paths);
            Require(list.Items.Count == 3 && list.SelectedItems.Count == 3, "Collecting must deduplicate and select the added items.");
            await Click((Button)panel.FindName("SelectButton"));
            Require(list.SelectedItems.Count == 0, "Clicking select-all again must deselect everything.");
            Require(!((Button)panel.FindName("RemoveButton")).IsEnabled && !((Button)panel.FindName("CopyButton")).IsEnabled, "Empty selection actions must be disabled.");
            await Click((Button)panel.FindName("SelectButton"));
            Require(list.SelectedItems.Count == 3, "Select all failed.");
            list.SelectedItems.RemoveAt(0);
            Require(((CheckBox)panel.FindName("SelectIndicator")).IsChecked is null, "Partial selection needs a mixed checkmark.");
            await Click((Button)panel.FindName("SelectButton"));
            Require(list.SelectedItems.Count == 3, "Partial selection must become all selected.");
            Select(list, paths[0], paths[2]);
            await Click((Button)panel.FindName("RemoveButton"));
            await Until(() => list.Items.Count == 1 && !Busy(panel));
            Require((await App.FileShelf.GetAsync()).SequenceEqual([paths[1]]), "Remove must affect only checked shelf items.");
            Require(paths.All(File.Exists), "Removing shelf items must keep original files.");
            await Click((Button)panel.FindName("UndoRemoveButton"));
            await Until(() => list.Items.Count == 3 && !Busy(panel));
            Require(list.SelectedItems.Count == 2, "Undo must restore and select the removed items.");
            panel.TryHandleShortcut(VirtualKey.A, true, false);
            Require(list.SelectedItems.Count == 3, "Ctrl+A failed.");
            panel.TryHandleShortcut(VirtualKey.A, true, true);
            Require(list.SelectedItems.Count == 0, "Ctrl+Shift+A failed.");
            Select(list, paths[1]);
            panel.TryHandleShortcut(VirtualKey.Delete, false, false);
            await Until(() => list.Items.Count == 2 && !Busy(panel));
            Require(paths.All(File.Exists), "Delete inside the shelf must preserve files.");
            panel.TryHandleShortcut(VirtualKey.Z, true, false);
            await Until(() => list.Items.Count == 3 && !Busy(panel));
            report["SelectionRemoveUndoKeyboard"] = true;

            Select(list, paths[0], paths[2]);
            await Guard(panel, () => panel.TransferToFolderAsync([paths[0], paths[2]], copyTo, false));
            Require(Directory.GetFiles(copyTo).Length == 2 && !File.Exists(Path.Combine(copyTo, Path.GetFileName(paths[1]))), "Copy must use only the selected subset.");
            Require((await App.FileShelf.GetAsync()).Count == 3 && paths.All(File.Exists), "Copy must preserve shelf references and sources.");
            await Guard(panel, () => panel.TransferToFolderAsync([paths[2]], moveTo, true));
            await Until(() => list.Items.Count == 2);
            Require(!File.Exists(paths[2]) && File.Exists(Path.Combine(moveTo, Path.GetFileName(paths[2]))), "Selected file was not moved.");
            Require(!(await App.FileShelf.GetAsync()).Contains(paths[2]), "A successful move must remove its old shelf reference.");
            report["CopyMoveOnlySelected"] = true;

            await App.SearchIndex.RebuildAsync(SearchIndexSettings.Sanitize([fixture], [], false, 4));
            OpenSearchPage(new SearchPageRequest("Project"));
            await Until(() => TabHost.Content is SearchResultsPage s && s.IsLoaded && !s.IsSearching && s.ResultRows.Any(r => r.Path == searchPath));
            var search = (SearchResultsPage)TabHost.Content;
            var results = (FileDetailsSurface)search.FindName("Results");
            results.TrySelectByPath(searchPath);
            await search.RunActionAsync(AppCommandId.ShowShelf);
            await Until(() => ((ContentControl)search.FindName("ShelfContent")).Content is FileShelfPanel { IsLoaded: true });
            var searchPanel = (FileShelfPanel)((ContentControl)search.FindName("ShelfContent")).Content;
            var searchList = (ListView)searchPanel.FindName("PathsList");
            await Click((Button)searchPanel.FindName("AddSelectionButton"));
            await Until(() => searchList.Items.Count == 3 && !Busy(searchPanel));
            await search.RunActionAsync(AppCommandId.AddToShelf);
            Require(((Border)search.FindName("ShelfCard")).Visibility == Visibility.Visible, "Adding from search must not toggle the shelf closed.");
            Require((await App.FileShelf.GetAsync()).Count == 3, "Adding the same search result must not duplicate it.");
            var data = new DataPackage();
            data.SetStorageItems([await StorageFile.GetFileFromPathAsync(dropPath)]);
            Require(FileShelfPanel.CanAccept(data.GetView()), "Search drag storage data was rejected.");
            await searchPanel.AddDataAsync(data.GetView());
            Require(searchList.Items.Count == 4, "Storage drop data did not collect the result.");
            data.Properties[FileShelfPanel.DragMarker] = true;
            Require(!FileShelfPanel.CanAccept(data.GetView()), "Shelf must not accept a drop onto itself.");
            Select(searchList, searchPath);
            searchPanel.TryHandleShortcut(VirtualKey.Delete, false, false);
            await Until(() => searchList.Items.Count == 3 && !Busy(searchPanel));
            Require(File.Exists(searchPath) && results.SelectedPaths().SequenceEqual([searchPath]), "Shelf deletion affected the search selection or original file.");
            await Click((Button)searchPanel.FindName("UndoRemoveButton"));
            await Until(() => searchList.Items.Count == 4 && !Busy(searchPanel));
            report["SearchCollectAddWhileOpenStorageDrop"] = true;

            foreach (var theme in new[] { AppThemeKind.Light, AppThemeKind.Dark })
            {
                await App.AppearanceViewModel.SetThemeAsync(theme);
                Select(searchList, paths[0], searchPath);
                await Task.Delay(200);
                var card = (Border)search.FindName("ShelfCard");
                var footer = (Grid)searchPanel.FindName("ActionsRow");
                var footerBounds = footer.TransformToVisual(card).TransformBounds(new(0, 0, footer.ActualWidth, footer.ActualHeight));
                Require(Math.Abs(card.ActualHeight - footerBounds.Bottom - 13) <= 1, "Collapsed feedback leaves extra footer space.");
                foreach (var name in new[] { "SelectButton", "AddSelectionButton", "CopyButton", "MoveButton", "RemoveButton" })
                {
                    var button = (Button)searchPanel.FindName(name);
                    var bounds = button.TransformToVisual(card).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                    Require(bounds.Right <= card.ActualWidth + 1 && bounds.Bottom <= card.ActualHeight + 1, "Shelf clips action " + name);
                    Require(!PolishDescendants(button).OfType<TextBlock>().Any(t => t.IsTextTrimmed), "Shelf clips action text " + name);
                }
                await Capture(card, "shelf-" + theme + ".png");
                await Capture(Content, "shelf-search-" + theme + ".png");
            }
            await search.RunActionAsync(AppCommandId.ShowShelf);
            await Until(() => ((Border)search.FindName("ShelfCard")).Visibility == Visibility.Collapsed);
            Require(((Border)search.FindName("ShelfCard")).Visibility == Visibility.Collapsed, "Shelf button must toggle the card closed.");
            await search.RunActionAsync(AppCommandId.ShowShelf);
            Require(searchList.SelectedItems.Count == 2, "Reopening the shelf must preserve selection.");
            var selectedBefore = searchList.SelectedItems.OfType<FileShelfPanel.ShelfItem>().Select(i => i.Path).ToArray();
            await searchPanel.ReloadAsync();
            Require(searchList.SelectedItems.OfType<FileShelfPanel.ShelfItem>().Select(i => i.Path).ToHashSet().SetEquals(selectedBefore), "Refresh discarded selection.");
            report["PreservedSelectionAndVisibleActions"] = true;

            // A pending close must not close the shelf again after it is reopened.
            var pending = new TaskCompletionSource();
            var operation = Guard(searchPanel, () => pending.Task);
            searchPanel.RequestClose();
            await Task.Delay(80);
            pending.SetResult();
            await operation;
            await Until(() => !searchPanel.IsOpen);
            await search.RunActionAsync(AppCommandId.ShowShelf);
            await Guard(searchPanel, () => Task.CompletedTask);
            Require(searchPanel.IsOpen, "A previous close request leaked into the next operation.");

            var extras = Enumerable.Range(1, 12).Select(i => Path.Combine(other, $"Project attachment {i:00}.txt")).ToArray();
            foreach (var path in extras) await File.WriteAllTextAsync(path, "Shelf scrolling fixture");
            await searchPanel.AddPathsAsync(extras);
            AppWindow.Resize(new(1470, 900));
            await Task.Delay(200);
            var smallCard = (Border)search.FindName("ShelfCard");
            var searchRoot = (Grid)search.FindName("SearchRoot");
            var cardBounds = smallCard.TransformToVisual(searchRoot).TransformBounds(new(0, 0, smallCard.ActualWidth, smallCard.ActualHeight));
            Require(cardBounds.Bottom <= searchRoot.ActualHeight, "Shelf extends below the smaller window.");
            var remove = (Button)searchPanel.FindName("RemoveButton");
            var actionBounds = remove.TransformToVisual(smallCard).TransformBounds(new(0, 0, remove.ActualWidth, remove.ActualHeight));
            Require(actionBounds.Bottom <= smallCard.ActualHeight, "A long shelf hides its actions in a smaller window.");
            Require(PolishDescendants(searchList).OfType<ScrollViewer>().Any(s => s.ScrollableHeight > 0), "Long shelves must scroll.");
            await Capture(Content, "shelf-small-window.png");
            var beforeClear = (await App.FileShelf.GetAsync()).ToArray();
            await Click((Button)searchPanel.FindName("MoreButton"));
            Require(searchPanel.ContainsFocus(), "Shelf menus must block shortcuts on the background file list.");
            await Click((MenuFlyoutItem)searchPanel.FindName("ClearButton"));
            await Until(() => searchList.Items.Count == 0 && !Busy(searchPanel));
            Require(beforeClear.All(File.Exists), "Clearing the shelf deleted files.");
            await Capture(smallCard, "shelf-empty.png");
            await Click((Button)searchPanel.FindName("UndoRemoveButton"));
            await Until(() => searchList.Items.Count == beforeClear.Length && !Busy(searchPanel));
            Require((await App.FileShelf.GetAsync()).ToHashSet().SetEquals(beforeClear), "Undo clear did not restore all references.");
            report["ReopenAfterBusyLongListSmallWindowClearUndo"] = true;
            report["Fixture"] = fixture;
            report["Passed"] = true;
        }
        catch (Exception error) { report["Passed"] = false; report["Error"] = error.ToString(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "shelf-smoke.json"), JsonSerializer.Serialize(report));

        static bool Busy(FileShelfPanel panel) => (bool)typeof(FileShelfPanel).GetField("_busy", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        static Task Guard(FileShelfPanel panel, Func<Task> action) => (Task)typeof(FileShelfPanel).GetMethod("GuardAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, [action])!;
        static void Select(ListView list, params string[] paths)
        {
            list.SelectedItems.Clear();
            foreach (var item in list.Items.OfType<FileShelfPanel.ShelfItem>()) if (paths.Contains(item.Path)) list.SelectedItems.Add(item);
        }
        static async Task Click(Control button)
        {
            if (!button.IsEnabled) throw new InvalidOperationException("Disabled button: " + button.Name);
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(80);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Until(Func<bool> predicate)
        {
            for (var i = 0; i < 200; i++) { if (predicate()) return; await Task.Delay(40); }
            throw new TimeoutException("Shelf UI did not reach its expected state.");
        }
    }
}
#endif
