using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Controls.Preview;
using FilesMate.App.Controls.Tags;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using FilesMate.App.Navigation;
using FilesMate.Platform.Windows.Operations;
using FilesMate.Platform.Windows.Processes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Loc = FilesMate.App.Localization.StringTable;

namespace FilesMate.App.Views;

public sealed partial class SearchResultsPage
{
    internal Task ApplyHistoryAsync(bool redo, Action<string> reportError) => _fileActions.ApplyUndoAsync(redo, reportError);
    private PaneFileActions _fileActions = null!;
    private QuickPreviewWindow? _quickPreview;
    private QuickPreviewDismissal? _quickPreviewInput;
    private FileShelfPanel? _shelfPanel;
    private readonly PinnedLocationStore _pins = new(Program.SettingsPath(PinnedLocationStore.DefaultFilePath));
    private bool _autoColumns = true, _fittingColumns;
    private readonly Dictionary<string, IReadOnlyList<TagDefinition>> _tags = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeFileSurface()
    {
        _fileActions = new(new WindowsLocalFileOperations(), Results.SelectedPaths, ActionFolder,
            Results.PrimaryPath, ShowError, () => _ = RefreshVisibleAsync(), App.VacateFoldersAsync, this);
        Results.ResolvePath = entry => _rows[entry.Id].Path;
        Results.ResolveFolder = ActionFolder;
        Results.IsPinnedPath = path => WindowsNavigationSource.IsPinned(path, _pins);
        Results.ResolveTags = entry => _tags.GetValueOrDefault(_rows[entry.Id].Path) ?? [];
        Results.CreateTagPicker = () => App.MetadataStore is { } store && App.FileIdentityProvider is { } identities
            ? TagPickerFlyout.CreatePanel(store, Results.SelectedPaths().Select(identities.Resolve).ToArray(), () => _ = LoadTagsAsync()) : null;
        Results.SetColumns([new(DetailsColumnId.Name, 260), new(DetailsColumnId.Location, 220),
            new(DetailsColumnId.Type, 90), new(DetailsColumnId.Size, 88), new(DetailsColumnId.Modified, 148), new(DetailsColumnId.Tags, 144)]);
        Results.SizeChanged += (_, _) => FitResultColumns();
        Results.SetLayout(FileLayoutKind.Details);
        Results.PresentationChanged += (_, _) => { if (!_fittingColumns) _autoColumns = false; };
        Results.SetAlphabetNavigationPolicy(false, 20, false, false);
        Results.RenameRequested = async (source, name) =>
        {
            var destination = await _fileActions.RenamePathAsync(source, name);
            if (destination is not null)
            {
                // Keep this result usable immediately, before the index catches up.
                var rows = _rows.Select((r, i) => r.Path.Equals(source, StringComparison.OrdinalIgnoreCase)
                    ? new SearchResultRow(r.Hit with { Path = destination, Name = System.IO.Path.GetFileName(destination) }, i) : r).ToArray();
                BindRows(rows); Results.TrySelectByPath(destination);
            }
            return destination;
        };
        Results.OpenRequested += async (_, entry) => await OpenAsync(_rows[entry.Id]);
        Results.OpenInNewTabRequested += (_, entry) => App.WindowForElement(this)?.OpenFolderInNewTab(_rows[entry.Id].Path);
        Results.RevealRequested += (_, _) => RevealSelected();
        Results.UpRequested += (_, _) => RevealSelected();
        Results.CopyPathRequested += (_, _) => CopyPaths(false);
        Results.RefreshRequested += async (_, _) => await SearchAsync(false);
        Results.SortRequested += (_, column) => SortResults(column);
        Results.SortSpecificationRequested += (_, sort) => SetResultSort(sort);
        Results.CommandRequested += (_, id) => _ = RunActionAsync(id);
        Results.SelectionChanged += (_, _) =>
        {
            _shelfPanel?.RefreshSourceSelection();
            UpdateStatusSelection();
            SyncCommands();
            if (_quickPreview is { } preview)
            {
                if (Results.Selection.Count == 1 && Results.PrimaryPath() is { } path) _ = preview.LoadAsync(path);
                else CloseQuickPreview();
            }
        };
        Results.QuickPreviewRequested += (_, _) => ToggleQuickPreview();
        Results.TerminalRequested += (_, path) => _ = new PaneFileActions(new WindowsLocalFileOperations(), () => [],
            () => path, () => null, ShowError, () => { }, App.VacateFoldersAsync, this).RunAsync(AppCommandId.OpenInTerminal);
        Results.DropRequested = request => request.TargetDirectory is { } target
            ? _fileActions.DropAsync(request.Paths, target, request.Operation, request.AllowSameDirectoryCopy) : Task.CompletedTask;
        Commands.CommandInvoked += (_, id) => _ = RunActionAsync(id);
        Commands.CopyPathClicked += (_, _) => CopyPaths(false);
        Commands.LayoutChanged += (_, layout) => { Results.SetLayout(layout); Commands.SetLayout(layout); };
        Commands.SortSpecificationRequested += (_, sort) => SetResultSort(sort);
        Commands.GroupingRequested += (_, grouping) => SetResultSort(SurfaceSort() with { Grouping = grouping });
        Commands.GridSizeRequested += (_, preset) => Results.SetGridSize(preset);
        Commands.ListZoomRequested += (_, zoom) => Results.SetListZoom(zoom);
        Results.PresentationChanged += (_, _) => { Commands.SetLayout(Results.LayoutKind); Commands.SetViewSize(Results.GridPreset, Results.ListZoomPercent); };
        Commands.PreviewClicked += (_, _) => ToggleQuickPreview();
        Commands.ShelfDragOver += ShelfDragOver;
        Commands.ShelfDrop += ShelfDrop;
        Commands.SetLayout(FileLayoutKind.Details);
    }

    private void FitResultColumns()
    {
        if (!_autoColumns || Results.ActualWidth <= 0) return;
        _fittingColumns = true;
        try
        {
            var available = Math.Max(240, Results.ActualWidth - 60);
            var type = available >= 884; var date = available >= 674;
            var remaining = available - 72 - 144 - (type ? 84 : 0) - (date ? 128 : 0);
            var name = Math.Max(116, remaining * .52); var location = Math.Max(96, remaining - name);
            Results.SetColumns([new(DetailsColumnId.Name, name), new(DetailsColumnId.Location, location),
                new(DetailsColumnId.Type, 84, type), new(DetailsColumnId.Size, 72), new(DetailsColumnId.Modified, 128, date), new(DetailsColumnId.Tags, 144)]);
        }
        finally { _fittingColumns = false; }
    }

    private string? ActionFolder()
    {
        var paths = Results.SelectedPaths();
        if (paths.Count > 0)
        {
            var parents = paths.Select(System.IO.Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (parents.Length == 1 && System.IO.Directory.Exists(parents[0])) return parents[0];
        }
        return System.IO.Directory.Exists(Request.Scope) ? Request.Scope : null;
    }
    private void SyncCommands()
    {
        Results.ClipboardHasFiles = PaneFileActions.ClipboardHasFiles();
        Results.IsFolderWritable = Results.Selection.Count > 0 || ActionFolder() is not null;
        Commands.ApplyContext(CommandContext.ForToolbar(Results.Selection.Count, Results.PrimaryIsDirectory(),
            Results.IsFolderWritable, Results.ClipboardHasFiles, primaryPath: Results.PrimaryPath(), shareAvailable: App.ShareService is not null));
    }
    private void Clipboard_Changed(object? sender, object e) => DispatcherQueue.TryEnqueue(SyncCommands);
    internal void ShowWhoLocks(string path) => _ = _fileActions.ShowWhoLocksAsync([path]);
    private void CopyPaths(bool quoted)
    {
        var paths = Results.SelectedPaths(); if (paths.Count == 0) return;
        var data = new DataPackage(); data.SetText(string.Join(Environment.NewLine, paths.Select(p => quoted ? $"\"{p}\"" : p)));
        Clipboard.SetContent(data);
        App.WindowForElement(this)?.ShowActionNotice(Loc.Get("Path_Copied"));
    }
    private void RevealSelected()
    {
        if (Results.PrimaryPath() is { } path && System.IO.Path.GetDirectoryName(path) is { } parent)
            App.WindowForElement(this)?.OpenSearchResultLocation(this, parent, path);
    }
    private async Task OpenAsync(SearchResultRow row)
    {
        try
        {
            if (row.Hit.IsDirectory) { App.WindowForElement(this)?.OpenSearchResultLocation(this, row.Path); return; }
            if (row.Hit.Application?.IsFilesMate == true) { App.WindowForElement(this)?.OpenFolderInNewTab(null!); return; }
            await ShellOperationWorker.RunAsync(() => DetachedProcess.Open(row.Hit.Path));
        }
        catch (Exception e) { ShowError(Loc.Get("Open_FailedPrefix") + e.Message); }
    }
    internal async Task RunActionAsync(AppCommandId command)
    {
        if (_disposed || IsSearching) return;
        try
        {
            if (command == AppCommandId.ShowShelf)
            {
                if (ShelfCard.Visibility == Visibility.Visible) _shelfPanel?.RequestClose();
                else await ShowShelfAsync();
                return;
            }
            if (command == AppCommandId.ManageTags) { App.WindowForElement(this)?.OpenSettings("tags"); return; }
            var paths = Results.SelectedPaths();
            if (command == AppCommandId.CopyPath) { CopyPaths(false); return; }
            if (command == AppCommandId.CopyPathQuoted) { CopyPaths(true); return; }
            if (command == AppCommandId.Rename && paths.Count == 1) { Results.BeginInlineRename(); return; }
            if (command == AppCommandId.AddToFavorites) { await App.Favorites.AddAsync(paths.Select(p => (p, System.IO.Directory.Exists(p)))); return; }
            if (command == AppCommandId.AddToShelf)
            {
                await ShowShelfAsync();
                if (_shelfPanel is not null) await _shelfPanel.AddPathsAsync(paths);
                return;
            }
            if (command == AppCommandId.AddTags && App.MetadataStore is { } store && App.FileIdentityProvider is { } identities)
            { TagPickerFlyout.Show(Results, store, paths.Select(identities.Resolve).ToArray(), () => _ = LoadTagsAsync()); return; }
            if (command == AppCommandId.Share && App.ShareService is { } share) { await share.ShareAsync(paths); return; }
            if (command == AppCommandId.OpenInNewWindow && paths.Count == 1) { App.WindowForElement(this)?.OpenFolderInNewWindow(paths[0]); return; }
            if (command is AppCommandId.PinToSidebar or AppCommandId.UnpinFromSidebar && paths.Count == 1)
            {
                if (command == AppCommandId.PinToSidebar) WindowsNavigationSource.Pin(paths[0], _pins);
                else WindowsNavigationSource.Unpin(paths[0], _pins);
                App.NotifyPinnedLocationsChanged(); return;
            }
            if (command is AppCommandId.ChooseFolderCover or AppCommandId.ResetFolderCover or AppCommandId.ResetFolderView)
            { await CustomizeFolderAsync(command); return; }
            await _fileActions.RunAsync(command);
        }
        catch (Exception error) { ShowError(error.Message); }
    }
    private async Task RefreshVisibleAsync()
    {
        // The file index is a snapshot. Remove vanished items after operations without
        // querying the same stale snapshot and resurrecting deleted or renamed rows.
        var generation = _generation; var rows = _rows;
        var remaining = await Task.Run(() => rows.Where(r => r.Hit.Application is not null || System.IO.Path.Exists(r.Path)).ToArray());
        if (_disposed || generation != _generation) return;
        BindRows(remaining);
        StatusLabel.Text = Loc.Get("SearchPage_RefreshAfterOperation");
        EmptyPanel.Visibility = remaining.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task LoadTagsAsync()
    {
        if (App.MetadataStore is not { } store || App.FileIdentityProvider is not { } identity) return;
        var generation = _generation;
        foreach (var row in _rows.ToArray())
        {
            var tags = await Task.Run(async () => await store.GetTagsAsync(identity.Resolve(row.Path), CancellationToken.None));
            if (_disposed || generation != _generation) return;
            _tags[row.Path] = tags;
        }
        Results.RefreshRealizedTags();
    }
    private async void ToggleQuickPreview()
    {
        if (_quickPreview is not null) { _quickPreview.Close(); return; }
        if (Results.Selection.Count != 1 || Results.PrimaryPath() is not { } path || App.WindowForElement(this) is not { } owner) return;
        var preview = _quickPreview = new QuickPreviewWindow(owner);
        preview.NavigateFile += (_, step) => Results.MovePreviewSelection(step);
        _quickPreviewInput = new(XamlRoot.Content, CloseQuickPreview);
        preview.Closed += (_, _) =>
        {
            if (_quickPreview == preview) { _quickPreview = null; _quickPreviewInput?.Dispose(); _quickPreviewInput = null; }
            if (_quickPreview is null && preview.RestoreOwnerFocus && IsLoaded && !_disposed) Results.Focus(FocusState.Programmatic);
        };
        preview.Activate(); await preview.LoadAsync(path);
    }
    private void CloseQuickPreview()
    {
        var preview = _quickPreview;
        _quickPreview = null;
        _quickPreviewInput?.Dispose(); _quickPreviewInput = null;
        preview?.Dismiss();
    }
    private async Task ShowShelfAsync()
    {
        if (_disposed || XamlRoot is null) return;
        if (_shelfPanel is null)
        {
            _shelfPanel = new FileShelfPanel(this, () => _ = RefreshVisibleAsync(), Results.SelectedPaths);
            _shelfPanel.CloseRequested += (_, _) => HideShelf();
            ShelfContent.Content = _shelfPanel;
        }
        ShelfCard.Visibility = Visibility.Visible;
        _shelfPanel.IsOpen = true;
        PositionShelf();
        await _shelfPanel.ReloadAsync();
    }
    private void HideShelf()
    {
        if (_shelfPanel is not null) _shelfPanel.IsOpen = false;
        ShelfCard.Visibility = Visibility.Collapsed;
    }
    private void PositionShelf()
    {
        if (_disposed || ShelfCard is null || ShelfCard.Visibility != Visibility.Visible || !Commands.IsLoaded) return;
        var anchor = Commands.ShelfAnchor;
        var point = anchor.TransformToVisual(SearchRoot).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight));
        var width = Math.Min(360, Math.Max(280, SearchRoot.ActualWidth - 24));
        ShelfCard.Width = width;
        ShelfCard.MaxHeight = Math.Max(180, SearchRoot.ActualHeight - point.Y - 20);
        ShelfCard.Margin = new Thickness(Math.Clamp(point.X + anchor.ActualWidth - width - 8, 4,
            Math.Max(4, SearchRoot.ActualWidth - width - 20)), point.Y - 2, 0, 0);
    }
    private void ShelfDragOver(object? sender, DragEventArgs e)
    {
        if (!FileShelfPanel.CanAccept(e.DataView)) { e.AcceptedOperation = DataPackageOperation.None; return; }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = Loc.Get("Shelf_Add");
        e.Handled = true;
        if (ShelfCard.Visibility != Visibility.Visible) _ = ShowShelfAsync();
    }
    private async void ShelfDrop(object? sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral(); e.Handled = true;
        try
        {
            await ShowShelfAsync();
            if (_shelfPanel is not null) await _shelfPanel.ReceiveDropAsync(e);
        }
        finally { deferral.Complete(); }
    }
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _shelfPanel?.ContainsFocus() == true || IsTextInput(e.OriginalSource as DependencyObject)) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var win = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            || Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && !alt && !win && e.Key == VirtualKey.Enter) { e.Handled = true; RevealSelected(); }
        else if (!ctrl && !shift && !alt && !win && e.Key == VirtualKey.Escape) e.Handled = HandleEscapeFromWindow();
        else
        {
            var modifiers = (ctrl ? 1 : 0) | (alt ? 2 : 0) | (shift ? 4 : 0) | (win ? 8 : 0);
            foreach (var (action, gesture) in App.Shortcuts.Snapshot())
                if ((int)gesture.Key == (int)e.Key && (int)gesture.Modifiers == modifiers)
                { e.Handled = InvokeWindowShortcut(action); break; }
        }
    }
    private static bool IsTextInput(DependencyObject? node)
    {
        for (; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node)) if (node is TextBox or ComboBox) return true;
        return false;
    }
    private async Task CustomizeFolderAsync(AppCommandId command)
    {
        var folder = Results.PrimaryIsDirectory() ? Results.PrimaryPath() : ActionFolder();
        if (!System.IO.Directory.Exists(folder)) return;
        if (command == AppCommandId.ResetFolderView) { await App.FolderCustomizations.SetViewAsync(folder!, null); return; }
        string? cover = null;
        if (command == AppCommandId.ChooseFolderCover)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".mp4", ".mkv", ".mov" }) picker.FileTypeFilter.Add(ext);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowForElement(this)!.NativeHandle);
            var file = await picker.PickSingleFileAsync(); if (file is null || _disposed) return;
            cover = file.Path;
            if (!string.Equals(FolderCustomizationStore.Key(System.IO.Path.GetDirectoryName(cover)), FolderCustomizationStore.Key(folder), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Loc.Get("Cover_DirectChild"));
        }
        await App.FolderCustomizations.SetCoverAsync(folder!, cover);
        Icons.FolderPreviewBinder.ClearCache(); App.NotifyFolderCoversChanged(); Results.RebindVisibleEntries();
    }
}
