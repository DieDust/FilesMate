using Loc = FilesMate.App.Localization.StringTable;
using System.Diagnostics;
using System.IO;

using FilesMate.App.Theming;
using FilesMate.App.Commands;
using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;
using FilesMate.Platform.Windows.Locks;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Shell;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace FilesMate.App.Views;

internal sealed partial class PaneFileActions
{
    private readonly ILocalFileOperations _operations;
    private readonly Func<IReadOnlyList<string>> _selectedPaths;
    private readonly Func<string?> _folderPath;
    private readonly Func<string?> _primaryPath;
    private readonly Action<string> _reportError;
    private readonly Action _refresh;
    private readonly Func<IReadOnlyList<string>, Task> _prepareDelete;
    private readonly FrameworkElement _host;
    private const string ClipboardTokenFormat = "FilesMate.ClipboardToken";
    private static bool _fileWorkActive;
    public Action<string>? NewItemCreated { get; set; }

    public PaneFileActions(
        ILocalFileOperations operations,
        Func<IReadOnlyList<string>> selectedPaths,
        Func<string?> folderPath,
        Func<string?> primaryPath,
        Action<string> reportError,
        Action refresh,
        Func<IReadOnlyList<string>, Task> prepareDelete,
        FrameworkElement host)
    {
        _operations = operations;
        _selectedPaths = selectedPaths;
        _folderPath = folderPath;
        _primaryPath = primaryPath;
        _reportError = reportError;
        _refresh = refresh;
        _prepareDelete = prepareDelete;
        _host = host;
    }

    public static bool ClipboardHasFiles()
    {
        try
        {
            var data = Clipboard.GetContent();
            return data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Bitmap) || data.Contains(StandardDataFormats.Text);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task RunAsync(AppCommandId id)
    {
        var mutates = id is AppCommandId.NewFolder or AppCommandId.NewFile or AppCommandId.Paste
            or AppCommandId.Rename or AppCommandId.BatchRename or AppCommandId.Recycle or AppCommandId.PermanentDelete
            or AppCommandId.NewFolderWithSelection or AppCommandId.CreateShortcut
            or AppCommandId.CompressZip or AppCommandId.Compress7z or AppCommandId.CompressNew
            or AppCommandId.ExtractHere or AppCommandId.ExtractToFolder or AppCommandId.ExtractToOther
            or AppCommandId.SmartExtract;
        if (mutates && (_fileWorkActive || FileOperationLifetime.IsBusy)) { _reportError(Loc.Get("Files_Busy")); return; }
        if (mutates) _fileWorkActive = true;
        using var lifetime = mutates ? FileOperationLifetime.Begin() : null;
        try
        {
            switch (id)
            {
                case AppCommandId.NewFolder:
                    Create("Command_NewFolder", directory: true);
                    break;
                case AppCommandId.NewFile:
                    Create("NewFileName", directory: false);
                    break;
                case AppCommandId.NewFolderWithSelection:
                    await GroupSelectionAsync();
                    break;
                case AppCommandId.Cut:
                    await SetClipboardAsync(cut: true);
                    break;
                case AppCommandId.Copy:
                    await SetClipboardAsync(cut: false);
                    break;
                case AppCommandId.Paste:
                    await PasteAsync();
                    break;
                case AppCommandId.Rename:
                    await RenameAsync();
                    break;
                case AppCommandId.BatchRename:
                    await BatchRenameAsync();
                    break;
                case AppCommandId.Recycle:
                    await RecycleSelectedAsync();
                    break;
                case AppCommandId.PermanentDelete:
                    await PermanentDeleteAsync();
                    break;
                case AppCommandId.Properties:
                    ShowProperties();
                    break;
                case AppCommandId.WhoLocks:
                    await ShowWhoLocksAsync();
                    break;
                case AppCommandId.OpenInTerminal:
                    OpenTerminal();
                    break;
                case AppCommandId.OpenWith:
                    await OpenWithAsync();
                    break;
                case AppCommandId.CreateShortcut:
                    CreateShortcut();
                    break;
                case AppCommandId.CompressZip:
                case AppCommandId.Compress7z:
                case AppCommandId.CompressNew:
                case AppCommandId.ExtractHere:
                case AppCommandId.ExtractToFolder:
                case AppCommandId.ExtractToOther:
                case AppCommandId.SmartExtract:
                case AppCommandId.OpenInCompactMate:
                    await RunArchiveAsync(id);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _reportError(Loc.Get("Files_OperationCancelled"));
        }
        catch (Exception ex)
        {
            _reportError(ex.Message);
        }
        finally { if (mutates) _fileWorkActive = false; }
    }

    public async Task DropAsync(
        IReadOnlyList<string> sources,
        string destinationDirectory,
        DataPackageOperation operation,
        bool allowSameDirectoryCopy = false)
    {
        if (_fileWorkActive || FileOperationLifetime.IsBusy) { _reportError(Loc.Get("Files_Busy")); return; }
        using var lifetime = FileOperationLifetime.Begin();
        _fileWorkActive = true;
        try
        {
            var move = operation == DataPackageOperation.Move;
            var result = await FileShelfTransfer.RunAsync(_operations, sources, destinationDirectory, move, allowSameDirectoryCopy: allowSameDirectoryCopy,
                resolveConflict: FileConflictDialog.For(_host));
            RecordTransfer(result, move);
            _refresh();
        }
        catch (Exception error)
        {
            _reportError(error.Message);
        }

        finally { _fileWorkActive = false; }
    }

    private void Create(string nameKey, bool directory)
    {
        var folder = RequireFolder();
        var name = StringTable.Get(nameKey);
        var path = UniquePath.CombineAvailable(folder, name, Path.Exists);
        if (directory)
        {
            _operations.CreateDirectory(path, failIfExists: true);
        }
        else
        {
            _operations.CreateEmptyFile(path);
        }

        RecordCreated([path]);
        _refresh();
        NewItemCreated?.Invoke(path);
    }

    private IReadOnlyList<string> SelectedOrFolder()
    {
        var paths = _selectedPaths();
        if (paths.Count > 0)
        {
            return paths;
        }

        var folder = _folderPath();
        return string.IsNullOrWhiteSpace(folder) ? [] : [folder];
    }

    private async Task SetClipboardAsync(bool cut)
    {
        var paths = SelectedOrFolder();
        if (paths.Count == 0)
        {
            return;
        }

        var items = new List<IStorageItem>(paths.Count);
        foreach (var path in paths)
        {
            items.Add(Directory.Exists(path)
                ? await StorageFolder.GetFolderFromPathAsync(path)
                : await StorageFile.GetFileFromPathAsync(path));
        }

        var data = new DataPackage
        {
            RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy,
        };
        data.SetStorageItems(items);
        data.SetData(ClipboardTokenFormat, Guid.NewGuid().ToString("N"));
        Clipboard.SetContent(data);
    }

    private Task PasteAsync() => PasteItemsAsync(Clipboard.GetContent());

    private async Task PasteItemsAsync(DataPackageView view)
    {
        var folder = RequireFolder();
        if (!view.Contains(StandardDataFormats.StorageItems))
        {
            await PasteContentAsync(folder, view);
            return;
        }

        var items = await view.GetStorageItemsAsync();
        var sources = items
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (sources.Length == 0)
        {
            return;
        }

        var move = view.RequestedOperation == DataPackageOperation.Move;
        var token = view.Contains(ClipboardTokenFormat) ? await view.GetDataAsync(ClipboardTokenFormat) as string : null;
        var result = await FileShelfTransfer.RunAsync(_operations, sources, folder, move, allowSameDirectoryCopy: !move,
            resolveConflict: FileConflictDialog.For(_host));
        RecordTransfer(result, move);
        if (move && result.Errors.Count == 0 && result.Skipped == 0 && !result.Cancelled)
        {
            view.ReportOperationCompleted(DataPackageOperation.Move);
            // A different application may have replaced the clipboard while copying.
            var current = Clipboard.GetContent();
            if (token is not null && current.Contains(ClipboardTokenFormat)
                && (await current.GetDataAsync(ClipboardTokenFormat) as string) == token) Clipboard.Clear();
        }

        _refresh();
    }

    private async Task RenameAsync()
    {
        var source = _primaryPath();
        if (string.IsNullOrEmpty(source))
        {
            source = _folderPath();
        }
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        var current = Path.GetFileName(source.TrimEnd('\\', '/'));
        var box = new TextBox { Text = current };
        var dialog = new ContentDialog
        {
            Title = StringTable.Get("Command_Rename"),
            Content = box,
            PrimaryButtonText = StringTable.Get("Command_Rename"),
            CloseButtonText = StringTable.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _host.XamlRoot,
        };
        ContentDialogTheme.Apply(dialog, _host);
        box.SelectAll();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var name = box.Text.Trim();
        if (string.IsNullOrEmpty(name) || name == current)
        {
            return;
        }

        await RenameCoreAsync(source, name);
    }

    private async Task BatchRenameAsync()
    {
        var paths = _selectedPaths();
        if (paths.Count < 2)
        {
            return;
        }

        var editor = new BatchRenameDialog();
        editor.SetSources(paths);
        var dialog = editor.CreateDialog(_host.XamlRoot);
        ContentDialogTheme.Apply(dialog, _host);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!editor.CanApply)
        {
            _reportError(editor.CurrentPlan.Errors.FirstOrDefault() ?? StringTable.Get("Error_InvalidName"));
            return;
        }

        var result = await new WindowsBatchRenameExecutor(_operations)
            .ExecuteAsync(editor.CurrentPlan);
        if (result.Errors.Count > 0)
        {
            _reportError(string.Join(Environment.NewLine, result.Errors));
            _refresh();
            return;
        }

        var pairs = editor.CurrentPlan.Entries
            .Where(entry => entry.RequiresRename)
            .Select(entry => new FilePathPair(entry.Source, entry.Target))
            .ToArray();
        if (pairs.Length > 0)
        {
            App.FileUndo.Push(await Task.Run(() => FileUndoRecord.Relocated(pairs)));
        }

        _refresh();
    }

    private async Task PermanentDeleteAsync()
    {
        var paths = _selectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        if (App.ExplorerPreferences.ConfirmPermanentDelete)
        {
            var dialog = new ContentDialog
            {
                Title = StringTable.Get("ConfirmDeleteTitle"),
                Content = StringTable.Get("ConfirmDeleteBody"),
                PrimaryButtonText = StringTable.Get("Command_PermanentDelete"),
                CloseButtonText = StringTable.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _host.XamlRoot,
            };
            ContentDialogTheme.Apply(dialog, _host);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await _prepareDelete(paths).ConfigureAwait(true);
        try { await ShellOperationWorker.RunAsync(() => _operations.PermanentDelete(paths)); }
        finally { _refresh(); }
    }

    public void Undo() => ApplyUndo(redo: false);

    public void Redo() => ApplyUndo(redo: true);

    private async Task RecycleSelectedAsync()
    {
        var paths = _selectedPaths().Where(Path.Exists).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        await _prepareDelete(paths).ConfigureAwait(true);
        var completed = new List<RecycleItemResult>();
        try
        {
            await ShellOperationWorker.RunAsync(() => _operations.Recycle(paths, completed.Add));
        }
        finally
        {
            // Shell deletion may finish some items before a cancellation/error.
            // Keep their undo record and refresh even when the call throws.
            var recycled = completed.Where(item => item.IsRecycled).ToArray();
            var permanent = completed.Count(item => !item.IsRecycled);
            if (permanent > 0) App.FileUndo.Clear();
            var record = recycled.Length > 0 ? FileUndoRecord.RecycledWithReceipts(recycled) : null;
            if (record is not null) App.FileUndo.Push(record);
            _refresh();
            if (permanent > 0 && _host is NavigatorPage page)
                page.ShowPermanentDeletionFeedback(permanent, record);
        }
    }

    private async void ApplyUndo(bool redo)
    {
        if (_fileWorkActive || FileOperationLifetime.IsBusy) { _reportError(Loc.Get("Files_Busy")); return; }
        using var lifetime = FileOperationLifetime.Begin();
        _fileWorkActive = true;
        try
        {
            await App.FileUndo.TryApplyAsync(_operations, redo, action => ShellOperationWorker.RunAsync(action));
        }
        catch (UndoStateChangedException)
        {
            _reportError(Loc.Get("Files_UndoChanged"));
        }
        catch (IrreversibleDeletionException error)
        {
            if (_host is NavigatorPage page) page.ShowPermanentDeletionFeedback(error.Count, null);
            _reportError(Loc.Format("Files_PermanentlyDeletedCount", error.Count));
        }
        catch (OperationCanceledException)
        {
            _reportError(Loc.Get("Files_OperationCancelled"));
        }
        catch (Exception ex)
        {
            _reportError(ex.Message);
        }
        finally { _fileWorkActive = false; _refresh(); }
    }

    private static void RecordCreated(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        App.FileUndo.Push(FileUndoRecord.Created(paths));
    }

    private void RecordTransfer(ShelfTransferResult result, bool move)
    {
        if (result.WithoutUndo > 0) App.FileUndo.Clear();
        if (result.Undo is not null) App.FileUndo.Push(result.Undo);
        if (_host is NavigatorPage page) page.ShowTransferFeedback(result);
        if (TransferFeedback.NeedsAttention(result)) _reportError(TransferFeedback.Format(result));
    }

    private static void RecordRelocated(IReadOnlyList<string> sources, IReadOnlyList<string> destinations)
    {
        if (sources.Count == 0 || sources.Count != destinations.Count)
        {
            return;
        }

        var pairs = new FilePathPair[sources.Count];
        for (var i = 0; i < sources.Count; i++)
        {
            pairs[i] = new FilePathPair(sources[i], destinations[i]);
        }

        App.FileUndo.Push(FileUndoRecord.Relocated(pairs));
    }

    private void ShowProperties()
    {
        var path = _primaryPath();
        if (string.IsNullOrEmpty(path))
        {
            path = _folderPath();
        }

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _operations.ShowProperties(path);
    }

    public Task ShowWhoLocksAsync() => ShowWhoLocksAsync(null);

    public async Task ShowWhoLocksAsync(IReadOnlyList<string>? paths)
    {
        paths ??= SelectedOrFolder();
        if (paths.Count == 0)
        {
            return;
        }

        var panel = new FileLockDialog();
        if (_host is not NavigatorPage page)
        {
            return;
        }

        panel.ReleasePreviewAsync = async () =>
        {
            panel.ReleaseIcons();
            await _prepareDelete(panel.Paths).ConfigureAwait(true);
        };
        panel.DeleteRequested += async (_, _) =>
        {
            if (FileOperationLifetime.IsBusy) { _reportError(Loc.Get("Files_Busy")); return; }
            using var lifetime = FileOperationLifetime.Begin();
            var targets = panel.TargetsToDelete();
            try
            {
                panel.ReleaseIcons();
                await _prepareDelete(targets).ConfigureAwait(true);
                await Task.Run(() =>
                {
                    _operations.PermanentDelete(targets);
                }).ConfigureAwait(true);
                page.HideLockOverlay();
                _refresh();
            }
            catch (Exception ex)
            {
                _reportError(ex.Message);
                await panel.SetPathsAsync(panel.Paths).ConfigureAwait(true);
                if (targets.Any(Path.Exists))
                {
                    panel.ShowError(StringTable.Format("Lock_DeleteFailed", ex.Message));
                }
            }
        };
        _ = panel.SetPathsAsync(paths);
        await page.ShowLockOverlayAsync(panel).ConfigureAwait(true);
    }

    private void OpenTerminal()
    {
        var folder = _primaryPath();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            folder = _folderPath();
        }

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new IOException(StringTable.Get("Error_NoFolder"));
        }

        TerminalLaunch.Open(folder);
    }

    private async Task OpenWithAsync()
    {
        var path = _primaryPath();
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
        {
            return;
        }

        var file = await StorageFile.GetFileFromPathAsync(path);
        var options = new LauncherOptions
        {
            DisplayApplicationPicker = true,
        };
        await Launcher.LaunchFileAsync(file, options);
    }

    private void CreateShortcut()
    {
        var path = _primaryPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var link = ShellShortcut.UniqueLinkPath(path);
        ShellShortcut.Create(path, link);
        RecordCreated([link]);
        _refresh();
    }

    private async Task RunArchiveAsync(AppCommandId id)
    {
        if (CompactMateSession.VerbFor(id) is not CompactMateVerb verb)
        {
            return;
        }

        var paths = _selectedPaths();
        if (paths.Count == 0)
        {
            return;
        }

        var result = await ArchiveOperationUI.RunAsync(_host, verb, paths, _folderPath(), _operations);
        if (result is null) return;
        RecordTransfer(ArchiveOperationUI.AsShelfResult(result), move: false);
        if (_host.IsLoaded) _refresh();
    }

    private string RequireFolder()
    {
        var folder = _folderPath();
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new IOException(StringTable.Get("Error_NoFolder"));
        }

        return folder;
    }
}
