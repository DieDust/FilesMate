#if FILESMATE_UI_TEST
using System.Reflection;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    internal async Task RunConvenienceChecksAsync(Func<string, Func<Task>, Task> check)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "convenience-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        ShellRoot.Children.Add(host);
        var surface = new FileDetailsSurface { ResolveFolder = () => folder, ResolvePath = e => Path.Combine(folder, e.Name) };
        host.Content = surface;
        surface.SetLayout(FileLayoutKind.Details);
        var errors = new List<string>();
        var actions = new PaneFileActions(new WindowsLocalFileOperations(), surface.SelectedPaths, () => folder,
            () => surface.SelectedPaths().FirstOrDefault(), errors.Add, Bind, _ => Task.CompletedTask, this);
        surface.RenameRequested = actions.RenamePathAsync;
        File.WriteAllText(Path.Combine(folder, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "beta");
        Bind();
        try
        {
            await Task.Delay(300);
            await check("FileIconStyleSwitchesLiveAndRejectsStaleLoads", async () =>
            {
                var vm = App.AppearanceViewModel!;
                var previous = vm.Current.UseBundledFileIcons;
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
                var image = new Image { Width = 40, Height = 40 };
                var fallback = new FontIcon { Glyph = "\uE8A5" };
                panel.Children.Add(image); panel.Children.Add(fallback);
                ShellRoot.Children.Add(panel);
                var sample = Path.Combine(folder, "associated.pdf"); File.WriteAllText(sample, "icon-only fixture");
                try
                {
                    await vm.SetUseBundledFileIconsAsync(true);
                    Icons.ShellIconBinder.BindPath(image, fallback, sample, false, 40);
                    Require(image.Source is Microsoft.UI.Xaml.Media.Imaging.SvgImageSource, "bundled default missing");
                    panel.UpdateLayout();
                    await Task.Delay(200);
                    await Capture(panel, "file-icons-bundled.png");
                    await vm.SetUseBundledFileIconsAsync(false);
                    await Wait(() => image.Source is Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap);
                    await Capture(panel, "file-icons-system.png");
                    var drag = await FileDragPreview.LoadIconAsync(sample, false, 40, XamlRoot);
                    Require(drag is Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap, "drag icon ignored system preference");
                    await vm.SetUseBundledFileIconsAsync(true);
                    await vm.SetUseBundledFileIconsAsync(false);
                    await vm.SetUseBundledFileIconsAsync(true);
                    await Task.Delay(250);
                    Require(image.Source is Microsoft.UI.Xaml.Media.Imaging.SvgImageSource, "late system completion overwrote bundled icon");
                }
                finally
                {
                    Icons.ShellIconBinder.Clear(image, fallback);
                    ShellRoot.Children.Remove(panel);
                    await vm.SetUseBundledFileIconsAsync(previous);
                }
            });
            await check("UnprotectedReplacementRequiresSeparateConfirmation", async () =>
            {
                var source = Path.Combine(folder, "budget-source.txt");
                var target = Path.Combine(folder, "budget-target.txt");
                File.WriteAllText(source, "incoming"); File.WriteAllText(target, "original");
                var budget = new ReplacementBackupBudget(4, 4);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var pending = WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)],
                        false, FileConflictDialog.For(this), backupBudget: budget);
                    var dialog = await Dialog();
                    ChooseConflict(dialog, FileConflictAction.ReplaceWithoutUndo);
                    Require(!Descendants(dialog).OfType<CheckBox>().Single().IsEnabled, "irreversible choice allowed apply-all");
                    Require(dialog.DefaultButton == ContentDialogButton.Close, "Enter authorized irreversible replacement");
                    InvokePrimary(dialog);
                    await Wait(() => !dialog.IsLoaded);
                    var confirmation = await Dialog();
                    Require(!ReferenceEquals(confirmation, dialog) && confirmation.DefaultButton == ContentDialogButton.Close,
                        "separate confirmation missing");
                    Require(File.ReadAllText(target) == "original", "target changed before final confirmation");
                    await Capture(confirmation, "backup-no-undo-confirmation.png");
                    if (attempt == 0) confirmation.Hide(); else InvokePrimary(confirmation);
                    var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
                    Require(result.Errors.Count == 0 && result.Undo is null && budget.UsedBytes == 0, "unexpected backup or failure");
                    Require(attempt == 0 ? result.Cancelled && File.ReadAllText(target) == "original"
                        : result.WithoutUndo == 1 && File.ReadAllText(target) == "incoming", "confirmation outcome incorrect");
                }
            });
            await check("BackupManagementClearsOnlyHistoryVersions", async () =>
            {
                var source = Path.Combine(folder, "managed-source.txt");
                var target = Path.Combine(folder, "managed-target.txt");
                File.WriteAllText(source, "new contents"); File.WriteAllText(target, "old contents");
                var result = await WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)], false,
                    (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
                Require(result.Undo is not null && ReplacementBackupBudget.Shared.UsedBytes > 0, "backup was not registered");
                App.FileUndo.Push(result.Undo!);
                var pending = BackupHistoryDialog.ShowAsync(this);
                var dialog = await Dialog();
                Require(Descendants(dialog).OfType<Button>().Any(b => ToolTipService.GetToolTip(b) is string p && Directory.Exists(p)),
                    "backup location missing from management");
                await Capture(dialog, "backup-management.png");
                InvokePrimary(dialog);
                await Wait(() => !App.FileUndo.CanUndo && ReplacementBackupBudget.Shared.UsedBytes == 0);
                Require(File.ReadAllText(target) == "new contents" && File.ReadAllText(source) == "new contents",
                    "clearing backups deleted a current file");
                dialog.Hide(); await pending;
            });
            await check("DeleteWithoutSelectionAndPartialCancellation", async () =>
            {
                var deleteFolder = Directory.CreateDirectory(Path.Combine(folder, "delete-check")).FullName;
                var first = Path.Combine(deleteFolder, "first.txt");
                var second = Path.Combine(deleteFolder, "second.txt");
                File.WriteAllText(first, "first"); File.WriteAllText(second, "second");
                IReadOnlyList<string> selection = [];
                var ops = new RecycleSmokeOperations();
                var refreshes = 0;
                var notices = new List<string>();
                var deleteActions = new PaneFileActions(ops, () => selection, () => deleteFolder, () => null,
                    notices.Add, () => refreshes++, _ => Task.CompletedTask, this);
                await deleteActions.RunAsync(AppCommandId.Recycle);
                Require(ops.Calls == 0 && Directory.Exists(deleteFolder), "empty selection deleted the open folder");
                selection = [first, second];
                await deleteActions.RunAsync(AppCommandId.Recycle);
                Require(!File.Exists(first) && File.ReadAllText(second) == "second", "partial cancellation changed the wrong item");
                Require(refreshes == 1 && notices.Contains(Localization.StringTable.Get("Files_OperationCancelled")), "cancellation was silent or list was not refreshed");
                Require(App.FileUndo.Latest is { Kind: FileUndoKind.Recycled } record && record.Paths.SequenceEqual(new[] { first }), "partial delete lost its exact undo record");
                Require(App.FileUndo.TryUndo(ops) && File.ReadAllText(first) == "first", "completed deletion could not be undone");
            });
            await check("PermanentDeletionNeverOffersRecycleUndo", async () =>
            {
                var deletionFolder = Directory.CreateDirectory(Path.Combine(folder, "permanent-delete-check")).FullName;
                var first = Path.Combine(deletionFolder, "permanent.txt");
                var second = Path.Combine(deletionFolder, "recycled.txt");
                foreach (var mixed in new[] { false, true })
                {
                    File.WriteAllText(first, "permanent fixture");
                    if (mixed) File.WriteAllText(second, "recycled fixture");
                    var ops = new RecycleSmokeOperations { PermanentFirst = true, CompleteAll = true };
                    var deletion = new PaneFileActions(ops, () => mixed ? new[] { first, second } : new[] { first },
                        () => deletionFolder, () => first, _ => { }, () => { }, _ => Task.CompletedTask, this);
                    App.FileUndo.Push(FileUndoRecord.Created([first]));
                    await deletion.RunAsync(AppCommandId.Recycle);
                    await Task.Delay(100);
                    Require(_transferResultNotice is { IsOpen: true } && _transferResultNotice.Title.Contains("1"), "permanent result missing");
                    if (mixed)
                    {
                        Require(App.FileUndo.Latest is { Kind: FileUndoKind.Recycled } record && record.Paths.SequenceEqual(new[] { second }), "mixed delete has wrong undo subset");
                        Require(_transferResultNotice!.ActionButton is Button, "mixed result cannot restore recycled subset");
                        Require(App.FileUndo.TryUndo(ops) && File.Exists(second) && !File.Exists(first), "mixed undo touched permanent item");
                    }
                    else Require(!App.FileUndo.CanUndo && !App.FileUndo.CanRedo && _transferResultNotice!.ActionButton is null, "permanent deletion offered fake undo");
                }
                App.FileUndo.Clear();
                _transferResultNotice!.IsOpen = false;
            });
            await check("InlineRenameAndTab", async () =>
            {
                Require(surface.TrySelectByName("a.txt"), "select a");
                surface.BeginInlineRename();
                await Wait(() => surface.IsRenaming);
                var editor = Field<TextBox>(surface, "_renameEditor");
                Require(editor.SelectedText == "a", "extension was selected");
                await Capture(surface, "inline-rename.png");
                editor.Text = "renamed.txt";
                await (Task)Call(surface, "CommitInlineRenameAsync", 1)!;
                await Wait(() => surface.IsRenaming);
                Require(Field<TextBox>(surface, "_renameEditor").Text == "b.txt", "Tab did not advance to the next original item");
                Call(surface, "CancelInlineRename");
                Require(File.ReadAllText(Path.Combine(folder, "renamed.txt")) == "alpha", "rename failed");
                Require(!surface.IsRenaming, "cancel left an editor");
                var conflict = actions.RenamePathAsync(Path.Combine(folder, "renamed.txt"), "b.txt");
                var conflictDialog = await Dialog();
                Require(!Descendants(conflictDialog).OfType<RadioButton>().Any(r => r.Tag is FileConflictAction.Merge), "file conflict offered a folder merge");
                conflictDialog.Hide();
                Require(await conflict is null, "rename overwrote an existing file");
                Require(File.ReadAllText(Path.Combine(folder, "b.txt")) == "beta", "destination content changed");
                errors.Clear();
            });
            await check("FolderRenameConflictSkipAndMerge", async () =>
            {
                var existing = Path.Combine(folder, "existing-folder");
                Directory.CreateDirectory(existing);
                File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");
                string? fresh = null;
                actions.NewItemCreated = path => fresh = path;
                await actions.RunAsync(AppCommandId.NewFolder);
                actions.NewItemCreated = null;
                Require(fresh is not null && Directory.Exists(fresh), "new folder was not created");
                Require(surface.TrySelectByName(Path.GetFileName(fresh!)), "new folder was not selectable");
                surface.BeginInlineRename(); await Wait(() => surface.IsRenaming);
                Field<TextBox>(surface, "_renameEditor").Text = "existing-folder";
                var pending = (Task)Call(surface, "CommitInlineRenameAsync", 0)!;
                var dialog = await Dialog();
                Require(dialog.DefaultButton == ContentDialogButton.Primary
                    && Descendants(dialog).OfType<RadioButton>().Single(r => r.IsChecked == true).Tag is FileConflictAction.Skip,
                    "Enter implicitly merges folders");
                await Capture(dialog, "folder-name-conflict.png");
                ChooseConflict(dialog, FileConflictAction.Skip); InvokePrimary(dialog); await pending;
                Require(Directory.Exists(fresh) && File.ReadAllText(Path.Combine(existing, "keep.txt")) == "keep", "skip changed content");
                Require(surface.SelectedPaths().Contains(fresh), "skip lost the original selection");
                surface.BeginInlineRename(); await Wait(() => surface.IsRenaming);
                Field<TextBox>(surface, "_renameEditor").Text = "existing-folder";
                pending = (Task)Call(surface, "CommitInlineRenameAsync", 0)!;
                dialog = await Dialog(); ChooseConflict(dialog, FileConflictAction.Merge); InvokePrimary(dialog);
                await pending;
                Require(!Directory.Exists(fresh) && surface.SelectedPaths().Contains(existing), "merge did not select the existing folder");
                Require(File.ReadAllText(Path.Combine(existing, "keep.txt")) == "keep", "merge altered existing content");
                Require(App.FileUndo.TryUndo(new WindowsLocalFileOperations()) && Directory.Exists(fresh), "merge undo did not restore empty folder");
                Require(errors.Count == 0, "expected conflict was reported as an operation error");
            });
            await check("OperationErrorKeepsFileList", () =>
            {
                var store = ViewModel.Store;
                var count = ViewModel.ItemCount;
                ViewModel.ReportUserError("Expected operation error");
                Require(ViewModel.ErrorText is null && ReferenceEquals(store, ViewModel.Store) && ViewModel.ItemCount == count,
                    "operation failure replaced the directory state");
                return Task.CompletedTask;
            });
            await check("PasteConflictSkipAndKeepBoth", async () =>
            {
                var incoming = Directory.CreateDirectory(Path.Combine(folder, "paste-source")).FullName;
                File.WriteAllText(Path.Combine(incoming, "b.txt"), "incoming b");
                File.WriteAllText(Path.Combine(incoming, "renamed.txt"), "incoming renamed");
                var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                data.SetStorageItems(new[] { await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(incoming, "b.txt")) });
                var pending = (Task)Call(actions, "PasteItemsAsync", data.GetView())!;
                var dialog = await Dialog();
                ChooseConflict(dialog, FileConflictAction.Skip); InvokePrimary(dialog); await pending;
                Require(File.ReadAllText(Path.Combine(folder, "b.txt")) == "beta" && !File.Exists(Path.Combine(folder, "b (2).txt")), "skip copied or changed a file");
                await Task.Delay(150);
                Require(_transferResultNotice is { IsOpen: true } && _transferResultNotice.Title.Contains("1"), "skip result disappeared after refresh");
                data.SetStorageItems(new[] {
                    await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(incoming, "b.txt")),
                    await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.Combine(incoming, "renamed.txt")) });
                pending = (Task)Call(actions, "PasteItemsAsync", data.GetView())!;
                dialog = await Dialog(); ChooseConflict(dialog, FileConflictAction.KeepBoth);
                Descendants(dialog).OfType<CheckBox>().Single().IsChecked = true;
                await Capture(dialog, "paste-file-conflict.png");
                InvokePrimary(dialog); await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Require(File.ReadAllText(Path.Combine(folder, "b (2).txt")) == "incoming b", "keep-both result missing");
                Require(File.ReadAllText(Path.Combine(folder, "renamed (2).txt")) == "incoming renamed", "apply-all did not cover the second conflict");
                Require(File.ReadAllText(Path.Combine(folder, "b.txt")) == "beta" && File.Exists(Path.Combine(incoming, "b.txt")), "copy altered existing or source data");
            });
            await check("PasteReplaceComparisonAndUndo", async () =>
            {
                var incoming = Path.Combine(folder, "paste-source", "b.txt");
                var target = Path.Combine(folder, "b.txt");
                var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                data.SetStorageItems(new[] { await Windows.Storage.StorageFile.GetFileFromPathAsync(incoming) });
                var pending = (Task)Call(actions, "PasteItemsAsync", data.GetView())!;
                var dialog = await Dialog();
                Require(Descendants(dialog).OfType<RadioButton>().Count() == 3, "missing replace/skip/keep-both choices");
                Require(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == incoming), "source comparison missing");
                Require(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == target), "target comparison missing");
                ChooseConflict(dialog, FileConflictAction.Replace);
                await Capture(dialog, "paste-replace-conflict.png");
                InvokePrimary(dialog); await pending.WaitAsync(TimeSpan.FromSeconds(10));
                Require(File.ReadAllText(target) == "incoming b", "replace did not publish source content");
                Require(App.FileUndo.TryUndo(new WindowsLocalFileOperations()), "replace missing undo");
                Require(File.ReadAllText(target) == "beta", "undo did not recover original");
                Require(App.FileUndo.TryRedo(new WindowsLocalFileOperations()), "replace missing redo");
                Require(File.ReadAllText(target) == "incoming b", "redo failed");
                Require(App.FileUndo.TryUndo(new WindowsLocalFileOperations()), "second undo failed");
            });
            await check("SelfPasteExplainsMissingReplace", async () =>
            {
                var target = Path.Combine(folder, "b.txt");
                var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                data.SetStorageItems(new[] { await Windows.Storage.StorageFile.GetFileFromPathAsync(target) });
                var pending = (Task)Call(actions, "PasteItemsAsync", data.GetView())!;
                var dialog = await Dialog();
                Require(dialog.Title as string == Localization.StringTable.Get("Transfer_SameItemTitle"), "self-copy reason missing");
                Require(!Descendants(dialog).OfType<RadioButton>().Any(r => r.Tag is FileConflictAction.Replace), "self-replacement offered");
                Require(Descendants(dialog).OfType<TextBlock>().Count(t => t.Text == target) == 1, "same path displayed twice");
                Require(((ScrollViewer)dialog.Content).ActualHeight < 300, "self-copy layout is too tall");
                await Capture(dialog, "paste-self-conflict.png");
                InvokePrimary(dialog); await pending;
                Require(File.ReadAllText(target) == "beta", "default skip changed the original");
                pending = (Task)Call(actions, "PasteItemsAsync", data.GetView())!;
                dialog = await Dialog(); ChooseConflict(dialog, FileConflictAction.KeepBoth);
                Require(dialog.PrimaryButtonText == Localization.StringTable.Get("Transfer_CreateCopy"), "copy action label incorrect");
                InvokePrimary(dialog); await pending;
                Require(File.ReadAllText(Path.Combine(folder, "b (3).txt")) == "beta", "self-copy did not allocate a numbered copy");
                Require(File.ReadAllText(target) == "beta", "self-copy changed the original");
            });
            await check("MergePromptsForNestedFileConflicts", async () =>
            {
                var source = Directory.CreateDirectory(Path.Combine(folder, "merge-incoming")).FullName;
                var target = Directory.CreateDirectory(Path.Combine(folder, "merge-existing")).FullName;
                File.WriteAllText(Path.Combine(source, "same.txt"), "incoming");
                File.WriteAllText(Path.Combine(target, "same.txt"), "existing");
                var pending = actions.RenamePathAsync(source, "merge-existing");
                var dialog = await Dialog(); ChooseConflict(dialog, FileConflictAction.Merge);
                Descendants(dialog).OfType<CheckBox>().Single().IsChecked = true;
                await Capture(dialog, "paste-folder-conflict.png"); InvokePrimary(dialog);
                await Wait(() => !dialog.IsLoaded);
                dialog = await Dialog();
                Require(!Descendants(dialog).OfType<RadioButton>().Any(r => r.Tag is FileConflictAction.Merge), "folder rule leaked into file conflict");
                ChooseConflict(dialog, FileConflictAction.KeepBoth); InvokePrimary(dialog);
                Require(await pending == target, "merged folder selection incorrect");
                Require(File.ReadAllText(Path.Combine(target, "same.txt")) == "existing", "merge overwrote existing file");
                Require(File.ReadAllText(Path.Combine(target, "same (2).txt")) == "incoming", "nested numbering missing");
                Require(App.FileUndo.TryUndo(new WindowsLocalFileOperations()), "merged move missing undo");
                Require(File.ReadAllText(Path.Combine(source, "same.txt")) == "incoming", "undo did not restore incoming file");
            });
            await check("ClipboardTextAndNoOverwrite", async () =>
            {
                var package = new DataPackage(); package.SetText("中文\nclipboard");
                var target = Path.Combine(folder, "clipboard.txt");
                await PaneFileActions.WriteClipboardContentAsync(package.GetView(), target, false);
                Require(File.ReadAllText(target) == "中文\nclipboard", "text encoding/content changed");
                try { await PaneFileActions.WriteClipboardContentAsync(package.GetView(), target, false); throw new InvalidOperationException("overwrite allowed"); }
                catch (IOException) { }
                Require(File.ReadAllText(target) == "中文\nclipboard", "existing file was removed");
            });
            await check("RejectedDropRetainsInspectableResult", async () =>
            {
                var source = Directory.CreateDirectory(Path.Combine(folder, "drop-source")).FullName;
                var target = Directory.CreateDirectory(Path.Combine(source, "child")).FullName;
                File.WriteAllText(Path.Combine(source, "keep.txt"), "keep");
                await actions.DropAsync([source], target, DataPackageOperation.Move);
                await Task.Delay(200);
                Require(_transferResultNotice is { IsOpen: true, Content: Expander }, "rejected drop disappeared without feedback");
                var details = (Expander)_transferResultNotice!.Content;
                details.IsExpanded = true;
                await Task.Delay(200);
                Require(Descendants(details).OfType<TextBlock>().Any(t => t.Text.Contains(source)), "failure details omitted source path");
                Require(File.ReadAllText(Path.Combine(source, "keep.txt")) == "keep", "invalid drop moved data");
                await Capture(_transferResultNotice, "transfer-result-details.png");
                _transferResultNotice.IsOpen = false;
            });
            await check("PartialTransferNoticeRetainsUndo", async () =>
            {
                var path = Path.Combine(folder, "partial-completed.txt");
                File.WriteAllText(path, "completed");
                var record = FileUndoRecord.Created([path]);
                App.FileUndo.Push(record);
                ShowTransferFeedback(new Services.ShelfTransferResult([new("incoming", path)], [], false) { Skipped = 1, Undo = record });
                await Task.Delay(200);
                Require(_transferResultNotice is { IsOpen: true, ActionButton: Button }, "partial result lost undo action");
                var button = (Button)_transferResultNotice!.ActionButton;
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                await Wait(() => !File.Exists(path));
                await Task.Delay(100);
                Require(!button.IsEnabled, "stale result could undo another operation");
                Require(App.FileUndo.TryRedo(new WindowsLocalFileOperations()) && File.ReadAllText(path) == "completed", "notice undo could not be redone");
                var next = Path.Combine(folder, "next-operation.txt");
                File.WriteAllText(next, "next"); App.FileUndo.Push(FileUndoRecord.Created([next]));
                await Task.Delay(150);
                Require(!_transferResultNotice.IsOpen && _operationNotice is { Visibility: Visibility.Visible }, "previous result obscured the next operation");
            });
            await check("ClipboardPng", async () =>
            {
                using var input = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, input);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 1, 1, 96, 96, [10, 20, 30, 255]);
                await encoder.FlushAsync(); input.Seek(0);
                var package = new DataPackage(); package.SetBitmap(RandomAccessStreamReference.CreateFromStream(input));
                var target = Path.Combine(folder, "clipboard.png");
                await PaneFileActions.WriteClipboardContentAsync(package.GetView(), target, true);
                using var file = File.OpenRead(target);
                using var stream = file.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                Require(decoder.PixelWidth == 1 && decoder.PixelHeight == 1, "PNG is not decodable");
            });
            await check("GroupDialogAndUndo", async () =>
            {
                Bind(); surface.RestoreSelectedNames(["renamed.txt", "b.txt"]);
                var pending = actions.RunAsync(AppCommandId.NewFolderWithSelection);
                var dialog = await Dialog();
                Descendants(dialog).OfType<TextBox>().First().Text = "organized";
                InvokePrimary(dialog);
                await pending;
                var target = Path.Combine(folder, "organized");
                Require(File.Exists(Path.Combine(target, "renamed.txt")) && File.Exists(Path.Combine(target, "b.txt")), string.Join(";", errors));
                await Task.Delay(100);
                Require(_operationNotice?.Visibility == Visibility.Visible, "completion feedback missing");
                File.WriteAllText(Path.Combine(target, "later.txt"), "keep");
                Require(App.FileUndo.TryUndo(new WindowsLocalFileOperations()), "undo unavailable");
                Require(File.Exists(Path.Combine(folder, "renamed.txt")) && File.ReadAllText(Path.Combine(target, "later.txt")) == "keep", "group undo lost data");
            });
            await check("FolderDragDwellAndCancel", async () =>
            {
                Bind(); var opens = 0;
                surface.OpenRequested += (_, _) => opens++;
                var items = Field<EntryItemsSource>(surface, "_items");
                var position = Enumerable.Range(0, items.Count).First(i => items.TryGetEntry(i, out var entry) && entry.Name == "organized");
                Set(surface, "_dropTargetViewIndex", position);
                Call(surface, "UpdateFolderHover", Path.Combine(folder, "organized"));
                await Task.Delay(150); Call(surface, "CancelFolderHover");
                await Task.Delay(800); Require(opens == 0, "leaving target did not cancel dwell");
                Call(surface, "UpdateFolderHover", Path.Combine(folder, "organized"));
                await Task.Delay(850); Require(opens == 1, "stable dwell did not open folder once");
            });
        }
        finally { host.Content = null; ShellRoot.Children.Remove(host); }

        await check("CommandPaletteFilter", async () =>
        {
            var pending = ShowCommandPaletteAsync();
            await Wait(() => _commandDialog?.IsLoaded == true);
            var dialog = _commandDialog!;
            Descendants(dialog).OfType<TextBox>().First().Text = "终端";
            await Task.Delay(100);
            var list = Descendants(dialog).OfType<ListView>().First();
            Require(list.Items.Count == 1, "filter did not narrow to terminal");
            await Capture(dialog, "command-palette.png");
            dialog.Hide(); await pending;
        });
        await check("CopyAndMoveOtherPane", async () =>
        {
            var sourceFolder = _leftVm.AddressText;
            var target = Path.Combine(sourceFolder, "other-pane");
            Directory.CreateDirectory(target);
            SetDualPane(true, persist: false);
            try
            {
                _rightVm!.Navigate(target);
                await Wait(() => !_rightVm.IsLoading && _rightVm.ViewIndex is not null);
                ActivateRight(false);
                Require(FileSurface.TrySelectByName("file12.txt"), "copy selection missing");
                var pending = TransferToOtherPaneAsync(false);
                var dialog = await Dialog();
                Require(((TextBlock)dialog.Content).Text.Contains(target), "confirmation hid destination");
                InvokePrimary(dialog); await pending;
                Require(File.Exists(Path.Combine(sourceFolder, "file12.txt")) && File.Exists(Path.Combine(target, "file12.txt")), "copy failed");
                var moveSource = Path.Combine(sourceFolder, "transfer-move.txt");
                File.WriteAllText(moveSource, "move fixture"); _leftVm.Refresh();
                await Wait(() => FileSurface.TrySelectByName("transfer-move.txt"));
                pending = TransferToOtherPaneAsync(true); dialog = await Dialog(); InvokePrimary(dialog); await pending;
                Require(!File.Exists(moveSource) && File.ReadAllText(Path.Combine(target, "transfer-move.txt")) == "move fixture", "move failed");
            }
            finally { SetDualPane(false, persist: false); }
        });
        await check("MemorySettings", async () =>
        {
            var settingsHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var settings = new GeneralPage(); settingsHost.Content = settings; ShellRoot.Children.Add(settingsHost);
            try
            {
                await Task.Delay(150);
                var combo = (ComboBox)settings.FindName("TabMemoryBox");
                Require(combo.Items.Count == 4 && combo.SelectedItem is not null, "memory modes missing");
                await Capture(settings, "memory-settings.png");
            }
            finally { ShellRoot.Children.Remove(settingsHost); settingsHost.Content = null; }
        });
        await check("PreviewSelectionLink", async () =>
        {
            Require(FileSurface.TrySelectByName("file12.txt"), "preview selection fixture missing");
            SetPreviewVisible(true); await Task.Delay(250);
            Require(_previewHost is { IsLoaded: true }, "preview did not load");
            Require(FileSurface.TrySelectByName("file13.txt"), "next file missing");
            await Task.Delay(250); SetPreviewVisible(false);
        });

        void Bind()
        {
            var store = new EntryStore(); var id = 0;
            store.Append(Directory.EnumerateFileSystemEntries(folder).Select(p => new FileEntryCore(++id, Path.GetFileName(p), 0, 1, 1,
                Directory.Exists(p) ? FileAttributes.Directory : FileAttributes.Normal, Directory.Exists(p) ? EntryKind.Directory : EntryKind.File)).ToArray());
            var index = EntryViewIndex.Build(store, EntrySort.Name, EntryFilter.None, NaturalStringComparer.Instance, 1);
            surface.Bind(store, index, 1);
        }
        async Task<ContentDialog> Dialog()
        {
            ContentDialog? found = null;
            await Wait(() => (found = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)
                .SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault()) is { IsLoaded: true });
            return found!;
        }
        static void InvokePrimary(ContentDialog dialog)
        {
            var button = Descendants(dialog).OfType<Button>().First(b => b.Name == "PrimaryButton");
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }
        static void ChooseConflict(ContentDialog dialog, FileConflictAction action) =>
            Descendants(dialog).OfType<RadioButton>().Single(r => r.Tag is FileConflictAction value && value == action).IsChecked = true;
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
        }
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
        static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
        static async Task Wait(Func<bool> ready)
        {
            for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(40); }
            throw new TimeoutException("UI not ready");
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static async Task Capture(UIElement element, string name)
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            var pixels = await bitmap.GetPixelsAsync();
            using var reader = DataReader.FromBuffer(pixels);
            var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
            using var file = File.Open(Path.Combine(AppContext.BaseDirectory, name), FileMode.Create);
            using var stream = file.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
            await encoder.FlushAsync();
        }
    }
    private sealed class RecycleSmokeOperations : ILocalFileOperations
    {
        public int Calls { get; private set; }
        public bool PermanentFirst { get; init; }
        public bool CompleteAll { get; init; }
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        {
            Calls++;
            if (PermanentFirst) File.Delete(paths[0]);
            else File.Move(paths[0], paths[0] + ".recycled");
            completed?.Invoke(new(paths[0], !PermanentFirst));
            if (!CompleteAll) throw new OperationCanceledException();
            foreach (var path in paths.Skip(1))
            {
                File.Move(path, path + ".recycled");
                completed?.Invoke(new(path, true));
            }
        }
        public void RestoreRecycled(IReadOnlyList<string> paths)
        { foreach (var path in paths) File.Move(path + ".recycled", path); }
        public void CreateDirectory(string path, bool failIfExists = false) => throw new NotSupportedException();
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> paths, string destination) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> paths, string destination) => throw new NotSupportedException();
        public void Rename(string source, string destination) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
#endif
