#if FILESMATE_UI_TEST
using FilesMate.App.Controls.FileSurface;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FilesMate.App.Controls.Omnibar;

public sealed partial class Omnibar
{
    internal async Task<object> RunFileDropSmokeAsync(string source, string parent)
    {
        var report = new Dictionary<string, object>();
        var target = _crumbViews.Single(v => (string)v.Tag == parent);
        Require(target.AllowDrop && target.IsLoaded && DropRequested is not null, "Breadcrumb is not connected to file operations");
        var file = Path.Combine(source, "move.txt");
        File.WriteAllText(file, "breadcrumb move");
        var folder = Directory.CreateDirectory(Path.Combine(source, "Folder", "Nested")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "Nested", "content.txt"), "nested content");
        var package = await Payload(true, file, folder);
        Require(CrumbFileOperation(package.GetView(), parent, false, false) == DataPackageOperation.Move, "Same-volume feedback must say Move");
        SetCrumbDropTarget(target);
        Require(target.Background is SolidColorBrush { Color.A: > 0 } && target.BorderBrush is SolidColorBrush { Color.A: > 0 }, "No drop highlight");
        await MainWindow.Capture(this, "breadcrumb-drop-highlight.png");
        SetCrumbDropTarget(null);
        Require(target.Background is SolidColorBrush { Color.A: 0 } && target.BorderBrush is null, "Drop highlight remained after leaving");
        Require(await DropFilesOnCrumbAsync(package.GetView(), parent, false, false) == DataPackageOperation.Move, "Breadcrumb rejected batch move");
        Require(!File.Exists(file) && File.ReadAllText(Path.Combine(parent, "move.txt")) == "breadcrumb move", "File did not move");
        Require(!Directory.Exists(folder) && File.ReadAllText(Path.Combine(parent, "Folder", "Nested", "content.txt")) == "nested content", "Folder did not move");
        report["BatchMoveAndHighlight"] = true;
        await App.FileUndo.TryApplyAsync(new WindowsLocalFileOperations(), false, action => ShellOperationWorker.RunAsync(action));
        Require(File.Exists(file) && Directory.Exists(folder) && !File.Exists(Path.Combine(parent, "move.txt")), "Move undo failed");
        App.FileUndo.Clear(); report["UndoMove"] = true;

        package = await Payload(true, file);
        Require(CrumbFileOperation(package.GetView(), parent, true, false) == DataPackageOperation.Copy, "Ctrl feedback must say Copy");
        Require(await DropFilesOnCrumbAsync(package.GetView(), parent, true, false) == DataPackageOperation.Copy, "Ctrl-copy rejected");
        Require(File.Exists(file) && File.ReadAllText(Path.Combine(parent, "move.txt")) == "breadcrumb move", "Copy lost original");
        report["ControlCopyPreservesSource"] = true;
        App.FileUndo.Clear();

        var external = Path.Combine(source, "external.txt"); File.WriteAllText(external, "external storage payload");
        var externalPackage = await Payload(false, external);
        Require(await DropFilesOnCrumbAsync(externalPackage.GetView(), parent, false, true) == DataPackageOperation.Move, "External Shift-move rejected");
        Require(!File.Exists(external) && File.Exists(Path.Combine(parent, "external.txt")), "External file did not move");
        report["ExternalStorageShiftMove"] = true;
        App.FileUndo.Clear();

        // Archive programs expose extracted temporary files through StorageItems.
        // Exercise that external copy path with a real ZIP and a native conflict dialog.
        var archiveInput = Directory.CreateDirectory(Path.Combine(source, "archive-input")).FullName;
        File.WriteAllText(Path.Combine(archiveInput, "config.ini"), "new archive configuration");
        var archive = Path.Combine(source, "update.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(archiveInput, archive);
        var archiveTemp = Path.Combine(source, "archive-temp");
        System.IO.Compression.ZipFile.ExtractToDirectory(archive, archiveTemp);
        var extractedFile = Path.Combine(archiveTemp, "config.ini");
        var existingFile = Path.Combine(parent, "config.ini");
        File.WriteAllText(existingFile, "existing configuration");
        var archivePackage = await Payload(false, extractedFile);
        var archiveCopy = DropFilesOnCrumbAsync(archivePackage.GetView(), parent, true, false);
        ContentDialog? conflict = null;
        for (var i = 0; i < 200; i++)
        {
            conflict = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault(d => d.IsLoaded);
            if (conflict is not null) break;
            if (archiveCopy.IsCompleted) throw new InvalidOperationException("Archive copy skipped the conflict dialog");
            await Task.Delay(25);
        }
        Require(conflict is not null, "Archive copy conflict dialog did not open");
        await Task.Delay(100);
        Require(File.ReadAllText(existingFile) == "existing configuration", "Archive copy replaced before asking");
        var replace = Descendants(conflict!).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "ConflictReplace");
        await MainWindow.Capture(Descendants(conflict!).OfType<Border>().First(b => b.Name == "BackgroundElement"), "archive-drop-conflict.png");
        ((IInvokeProvider)new ButtonAutomationPeer(replace).GetPattern(PatternInterface.Invoke)).Invoke();
        Require(await archiveCopy.WaitAsync(TimeSpan.FromSeconds(15)) == DataPackageOperation.Copy, "Archive replacement did not finish");
        Require(File.ReadAllText(existingFile) == "new archive configuration", "Archive content was not replaced");
        Require(!File.Exists(Path.Combine(parent, "config (2).ini")), "Archive copy still added a suffix");
        Require(File.Exists(archive) && File.Exists(extractedFile), "Archive copy removed its source");
        await App.FileUndo.TryApplyAsync(new WindowsLocalFileOperations(), false, action => ShellOperationWorker.RunAsync(action));
        Require(File.ReadAllText(existingFile) == "existing configuration", "Archive replacement undo lost the original");
        App.FileUndo.Clear(); report["ExternalArchiveCopyReplacementAndUndo"] = true;

        Require(await DropFilesOnCrumbAsync(package.GetView(), source, false, false) == DataPackageOperation.None, "Same-folder move accepted");
        Require(await DropFilesOnCrumbAsync(package.GetView(), "filesmate:home", false, false) == DataPackageOperation.None, "Home accepted files");
        var ancestor = await Payload(true, parent);
        Require(await DropFilesOnCrumbAsync(ancestor.GetView(), source, false, false) == DataPackageOperation.None, "Ancestor moved into its descendant");
        Require(File.Exists(file) && Directory.Exists(parent), "Rejected drop changed files");
        report["InvalidTargetsPreserveSources"] = true;

        ShowCrumbFolderItems([new("Child", source)], (Button)target.Children[1], parent);
        Require(CrumbFolderItems.Children.OfType<Button>().All(b => b.AllowDrop), "Folder dropdown does not accept drops");
        DismissCrumbFolders();
        report["FolderDropdownTargets"] = true;
        return report;

        static async Task<DataPackage> Payload(bool internalPaths, params string[] paths)
        {
            var items = new List<IStorageItem>();
            foreach (var path in paths)
                items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move };
            data.SetStorageItems(items);
            if (internalPaths) FileDropRequest.SetSourcePaths(data, paths);
            return data;
        }
        static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
#endif
