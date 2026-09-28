#if FILESMATE_UI_TEST
using System.Text.Json;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FilesMate.App;

public sealed partial class MainWindow
{
    private async Task RunTabFileDropSmokeAsync()
    {
        var results = new Dictionary<string, object>();
        var output = Path.Combine(AppContext.BaseDirectory, "tab-file-drop-smoke.json");
        var fixture = Path.Combine(AppContext.BaseDirectory, "test-profile", "tab-drop-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Move(new(-10000, -10000));
            var source = Directory.CreateDirectory(Path.Combine(fixture, "Source")).FullName;
            var target = Directory.CreateDirectory(Path.Combine(fixture, "Target")).FullName;
            var nested = Directory.CreateDirectory(Path.Combine(source, "Folder", "Nested")).FullName;
            var file = Path.Combine(source, "move.txt");
            File.WriteAllText(file, "move content");
            File.WriteAllText(Path.Combine(nested, "payload.txt"), "nested content");
            AddNavigatorTab(source);
            var sourceTab = (TabViewItem)Tabs.SelectedItem;
            await Ready(sourceTab);

            // Leave the new tab's lazy load pending until a file is dropped on it.
            AddNavigatorTab(target);
            var targetTab = (TabViewItem)Tabs.SelectedItem;
            Tabs.SelectedItem = sourceTab;
            Require(((NavigatorTabContent)targetTab.Tag).Navigator is null, "Target was not lazy");
            var package = await Payload(file, Path.GetDirectoryName(nested)!);
            Require(TabFileOperation(package.GetView(), FileTabDestination(targetTab), false, false) == DataPackageOperation.Move,
                "Same-volume drag feedback must say Move");
            Require(await DropFilesOnTabAsync(targetTab, package.GetView(), FileTabDestination(targetTab), false, false) == DataPackageOperation.Move,
                "New tab rejected the drop");
            Require(!File.Exists(file) && File.ReadAllText(Path.Combine(target, "move.txt")) == "move content", "File did not move");
            Require(!Directory.Exists(Path.GetDirectoryName(nested)) && File.ReadAllText(Path.Combine(target, "Folder", "Nested", "payload.txt")) == "nested content",
                "Folder contents did not move");
            results["LazyTabBatchMove"] = true;

            await App.FileUndo.TryApplyAsync(new WindowsLocalFileOperations(), false, action => ShellOperationWorker.RunAsync(action));
            Require(File.Exists(file) && Directory.Exists(nested) && !File.Exists(Path.Combine(target, "move.txt")), "Move undo failed");
            results["MoveUndo"] = true;
            App.FileUndo.Clear();

            Tabs.SelectedItem = sourceTab;
            await Ready(sourceTab);
            QueueFileTabHover(targetTab); await Task.Delay(100); CancelFileTabHover(); await Task.Delay(850);
            Require(ReferenceEquals(Tabs.SelectedItem, sourceTab), "Leaving a tab did not cancel hover");
            QueueFileTabHover(targetTab); await Task.Delay(900);
            Require(ReferenceEquals(Tabs.SelectedItem, targetTab), "Hover did not switch folders");
            results["HoverSwitchAndCancel"] = true;

            // The restored destination must be the last folder, not the path
            // originally used to create this tab.
            target = Directory.CreateDirectory(Path.Combine(target, "Restored folder")).FullName;
            ((NavigatorTabContent)targetTab.Tag).Navigator!.ViewModel.Navigate(target);
            await Ready(targetTab);
            Tabs.SelectedItem = sourceTab;
            await Ready(sourceTab);
            Require(await HibernateTabAsync(targetTab), "Target could not hibernate");
            package = await Payload(file);
            Require(await DropFilesOnTabAsync(targetTab, package.GetView(), FileTabDestination(targetTab), true, false) == DataPackageOperation.Copy,
                "Sleeping tab rejected Ctrl-copy");
            Require(File.Exists(file) && File.ReadAllText(Path.Combine(target, "move.txt")) == "move content", "Ctrl-copy removed the source");
            results["HibernatedTabCopy"] = true;

            var externalFile = Path.Combine(source, "external.txt");
            File.WriteAllText(externalFile, "external content");
            var external = new DataPackage();
            external.SetStorageItems([await StorageFile.GetFileFromPathAsync(externalFile)]);
            Require(await DropFilesOnTabAsync(targetTab, external.GetView(), target, false, true) == DataPackageOperation.Move,
                "External storage payload rejected");
            Require(!File.Exists(externalFile) && File.Exists(Path.Combine(target, "external.txt")), "External Shift-move failed");
            results["ExternalStorageDrop"] = true;

            var invalid = await Payload(Path.GetDirectoryName(nested)!);
            Require(TabFileOperation(invalid.GetView(), nested, false, false) == DataPackageOperation.None, "Folder accepted its own parent");
            Require(await DropFilesOnTabAsync(sourceTab, package.GetView(), source, false, false) == DataPackageOperation.None, "Same-directory move accepted");
            AddHomeTab(); var home = (TabViewItem)Tabs.SelectedItem;
            Require(await DropFilesOnTabAsync(home, package.GetView(), FileTabDestination(home), false, false) == DataPackageOperation.None, "Home accepted a file");
            CloseTab(home);
            AddNavigatorTab(target); var closed = (TabViewItem)Tabs.SelectedItem; CloseTab(closed);
            Require(await DropFilesOnTabAsync(closed, package.GetView(), target, false, false) == DataPackageOperation.None, "Closed tab accepted a file");
            Require(File.Exists(file), "Rejected drops changed their source");
            results["InvalidTargetsPreserveSources"] = true;
            results["Passed"] = true;
        }
        catch (Exception error) { results["Passed"] = false; results["Error"] = error.ToString(); }
        finally
        {
            CancelFileTabHover();
            App.FileUndo.Clear();
            File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit();
        }

        async Task Ready(TabViewItem tab)
        {
            for (int i = 0; i < 200; i++)
            {
                if (tab.Tag is NavigatorTabContent { RestoreState: null, Navigator: { IsLoaded: true } page }
                    && !page.ViewModel.IsLoading) { await Task.Delay(100); return; }
                await Task.Delay(25);
            }
            throw new TimeoutException("Navigator did not load");
        }
        static async Task<DataPackage> Payload(params string[] paths)
        {
            var items = new List<IStorageItem>();
            foreach (var path in paths)
                items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move };
            data.SetStorageItems(items); FileDropRequest.SetSourcePaths(data, paths); return data;
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
