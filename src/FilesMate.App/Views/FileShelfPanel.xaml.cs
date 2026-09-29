using FilesMate.App.Icons;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Localization;
using FilesMate.App.Services;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Associations;
using FilesMate.Platform.Windows.CompactMate;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FilesMate.App.Views;

public sealed partial class FileShelfPanel : UserControl
{
    internal const string DragMarker = FileDropRequest.ShelfMarker;
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed record ShelfItem(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
        public string Parent => System.IO.Path.GetDirectoryName(Path) ?? Path;
    }

    private readonly FrameworkElement _host;
    private readonly Action _refresh;
    private readonly Func<IReadOnlyList<string>> _sourceSelection;
    private readonly PaneFileActions _fileActions;
    private string[] _removedPaths = [];
    private readonly bool _ready;
    private CancellationTokenSource? _transfer;
    private bool _busy;
    private bool _dragging;
    private bool _closeRequested;
    private long _reloadVersion;
    public event EventHandler? CloseRequested;
    private bool _isOpen;
    internal bool IsOpen
    {
        get => _isOpen;
        set { _isOpen = value; if (value) _closeRequested = false; }
    }

    internal bool ContainsFocus()
    {
        if (!IsOpen || XamlRoot is null) return false;
        if (MoreButton.Flyout.IsOpen) return true;
        var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (current is not null)
        {
            if (ReferenceEquals(current, this)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    internal FileShelfPanel(FrameworkElement host, Action refresh, Func<IReadOnlyList<string>>? sourceSelection = null)
    {
        _host = host;
        _refresh = refresh;
        _sourceSelection = sourceSelection ?? (() => []);
        _fileActions = new(new WindowsLocalFileOperations(), Selected, () => null, () => Selected().FirstOrDefault(),
            SetStatus, refresh, App.VacateFoldersAsync, host);
        InitializeComponent();
        TitleText.Text = StringTable.Get("Shelf_Title");
        EmptyText.Text = StringTable.Get("Shelf_Empty");
        EmptyHint.Text = StringTable.Get("Shelf_EmptyHint");
        CopyButton.Content = StringTable.Get("Shelf_CopyToShort");
        MoveButton.Content = StringTable.Get("Shelf_MoveToShort");
        RemoveButton.Content = StringTable.Get("Shelf_RemoveShort");
        MoreButton.Content = StringTable.Get("Shelf_More");
        AddSelectionLabel.Text = StringTable.Get("Shelf_AddSelection");
        EmptyAddSelectionLabel.Text = StringTable.Get("Shelf_AddSelection");
        UndoRemoveButton.Content = StringTable.Get("Shelf_UndoRemove");
        CancelButton.Content = StringTable.Get("Cancel");
        Caption(ShelfCloseButton, "Close");
        Caption(CopyButton, "Shelf_CopyTo");
        Caption(MoveButton, "Shelf_MoveTo");
        Caption(CompressButton, "Command_Compress");
        Caption(SelectButton, "Command_SelectAll");
        Caption(RemoveButton, "Shelf_Remove");
        Caption(ClearButton, "Shelf_Clear");
        Caption(CancelButton, "Shelf_Cancel");
        Caption(AddSelectionButton, "Shelf_AddSelection");
        ToolTipService.SetToolTip(AddSelectionButton, StringTable.Get("Shelf_AddSelectionHint"));
        Caption(EmptyAddSelectionButton, "Shelf_AddSelection");
        ToolTipService.SetToolTip(EmptyAddSelectionButton, StringTable.Get("Shelf_AddSelectionHint"));
        Caption(CopyPathsButton, "Command_CopyPath");
        Caption(RevealButton, "Shelf_Reveal");
        Caption(UndoRemoveButton, "Shelf_UndoRemove");
        Theming.FlyoutTheme.FollowHost(MoreButton.Flyout);
        Loaded += (_, _) =>
        {
            _closeRequested = false;
            App.FileShelf.Changed += ShelfChanged;
            _ = ReloadAsync();
        };
        Unloaded += (_, _) =>
        {
            App.FileShelf.Changed -= ShelfChanged;
            _transfer?.Cancel();
            _reloadVersion++;
        };
        _ready = true;
        UpdateButtons();
    }

    private static void Caption(FrameworkElement button, string key)
    {
        var text = StringTable.Get(key);
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
        if (button is MenuFlyoutItem item) item.Text = text;
    }

    private void ShelfChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (IsLoaded && IsOpen && !_dragging) _ = ReloadAsync();
    });

    private string[] Selected() => PathsList.SelectedItems.OfType<ShelfItem>().Select(item => item.Path).ToArray();

    internal async Task ReloadAsync(IReadOnlyList<string>? selectPaths = null)
    {
        var revision = ++_reloadVersion;
        var paths = await App.FileShelf.GetAsync();
        if (revision != _reloadVersion || _dragging) return;
        // Read selection after the asynchronous load so a click made while loading is retained.
        var selected = Selected().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectPaths is not null) selected.UnionWith(selectPaths);
        var items = paths.Select(path => new ShelfItem(path)).ToArray();
        PathsList.ItemsSource = items;
        foreach (var item in items)
            if (selected.Contains(item.Path)) PathsList.SelectedItems.Add(item);
        CountText.Text = StringTable.Format("Shelf_Count", paths.Count);
        UpdateButtons();
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready) UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selected = PathsList.SelectedItems.Count > 0;
        var hasItems = PathsList.Items.Count > 0;
        var canAdd = _sourceSelection().Count > 0;
        SelectionRow.Visibility = ActionsRow.Visibility = MoreButton.Visibility = CountText.Visibility = PathsList.Visibility =
            hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        EmptyAddSelectionButton.Visibility = canAdd ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = MoveButton.IsEnabled = CompressButton.IsEnabled =
            RemoveButton.IsEnabled = !_busy && !_dragging && selected;
        ClearButton.IsEnabled = SelectButton.IsEnabled = !_busy && !_dragging && PathsList.Items.Count > 0;
        CopyPathsButton.IsEnabled = !_busy && !_dragging && selected;
        RevealButton.IsEnabled = !_busy && !_dragging && PathsList.SelectedItems.Count == 1;
        AddSelectionButton.IsEnabled = EmptyAddSelectionButton.IsEnabled = !_busy && !_dragging && canAdd;
        UndoRemoveButton.IsEnabled = !_busy && !_dragging;
        var all = PathsList.Items.Count > 0 && PathsList.SelectedItems.Count == PathsList.Items.Count;
        SelectIndicator.IsChecked = all ? true : selected ? null : false;
        SelectLabel.Text = StringTable.Get(all ? "Shelf_SelectNone" : "Command_SelectAll");
        Caption(SelectButton, all ? "Shelf_SelectNone" : "Command_SelectAll");
        SelectionText.Text = StringTable.Format("Shelf_SelectedCount", PathsList.SelectedItems.Count);
        PathsList.CanDragItems = !_busy;
        PathsList.IsEnabled = !_busy;
        CancelButton.Visibility = _transfer is not null ? Visibility.Visible : Visibility.Collapsed;
        UndoRemoveButton.Visibility = _removedPaths.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateFeedbackVisibility();
    }

    internal void RefreshSourceSelection() => UpdateButtons();

    private void UpdateFeedbackVisibility() => FeedbackRow.Visibility =
        StatusHost.Visibility == Visibility.Visible || CancelButton.Visibility == Visibility.Visible || UndoRemoveButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;

    private void SetStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        StatusHost.Visibility = StatusText.Visibility;
        UpdateFeedbackVisibility();
    }

    private void Schedule(Func<Task> action) => DispatcherQueue.TryEnqueue(() =>
    {
        if (IsLoaded) _ = GuardAsync(action);
    });

    private async Task GuardAsync(Func<Task> action)
    {
        if (_busy || _dragging) return;
        _busy = true;
        UpdateButtons();
        try { await action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            SetStatus(error.Message);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
            if (_closeRequested) CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => RequestClose();

    private void Shelf_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (TryHandleShortcut(e.Key, ctrl, shift)) e.Handled = true;
    }

    internal bool TryHandleShortcut(Windows.System.VirtualKey key, bool ctrl, bool shift)
    {
        if (key == Windows.System.VirtualKey.Escape) { RequestClose(); return true; }
        if (_busy || _dragging) return key == Windows.System.VirtualKey.Delete || ctrl;
        if (ctrl && key == Windows.System.VirtualKey.A)
        { if (shift) PathsList.SelectedItems.Clear(); else PathsList.SelectAll(); return true; }
        if (key == Windows.System.VirtualKey.Delete)
        {
            var paths = Selected();
            Schedule(() => RemovePathsAsync(paths));
            return true;
        }
        if (ctrl && key == Windows.System.VirtualKey.Z && _removedPaths.Length > 0)
        { Schedule(UndoRemoveAsync); return true; }
        if (ctrl && key is Windows.System.VirtualKey.C or Windows.System.VirtualKey.X)
        { Schedule(() => _fileActions.RunAsync(key == Windows.System.VirtualKey.C ? AppCommandId.Copy : AppCommandId.Cut)); return true; }
        if (ctrl && key == Windows.System.VirtualKey.V) { Schedule(AddClipboardAsync); return true; }
        return false;
    }

    internal void RequestClose() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_busy && !_dragging) CloseRequested?.Invoke(this, EventArgs.Empty);
        else
        {
            _closeRequested = true;
            _transfer?.Cancel();
        }
    });

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _dragging) return;
        if (PathsList.SelectedItems.Count == PathsList.Items.Count) PathsList.SelectedItems.Clear();
        else PathsList.SelectAll();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _transfer?.Cancel();
    private void Remove_Click(object sender, RoutedEventArgs e)
    { var paths = Selected(); Schedule(() => RemovePathsAsync(paths)); }
    private void Clear_Click(object sender, RoutedEventArgs e) => Schedule(async () => await RemovePathsAsync(await App.FileShelf.GetAsync()));
    private void UndoRemove_Click(object sender, RoutedEventArgs e) => Schedule(UndoRemoveAsync);
    private async Task RemovePathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        await App.FileShelf.RemoveAsync(paths);
        _removedPaths = paths.ToArray();
        await ReloadAsync();
        SetStatus(StringTable.Format("Shelf_Removed", paths.Count));
    }
    private async Task UndoRemoveAsync()
    {
        var paths = _removedPaths;
        await AddPathsAsync(paths);
        _removedPaths = [];
        UpdateButtons();
    }
    internal async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        await App.FileShelf.AddAsync(paths);
        await ReloadAsync(paths);
        SetStatus("");
    }
    private void AddSelection_Click(object sender, RoutedEventArgs e)
    { var paths = _sourceSelection().ToArray(); Schedule(() => AddPathsAsync(paths)); }
    private async Task AddClipboardAsync()
    {
        await AddDataAsync(Clipboard.GetContent());
    }
    internal async Task AddDataAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems)) return;
        var items = await data.GetStorageItemsAsync();
        await AddPathsAsync(items.Select(item => item.Path).ToArray());
    }
    private void CopyPaths_Click(object sender, RoutedEventArgs e)
    {
        var paths = Selected(); if (paths.Length == 0) return;
        Schedule(() => { var data = new DataPackage(); data.SetText(string.Join(Environment.NewLine, paths)); Clipboard.SetContent(data); return Task.CompletedTask; });
    }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is [var path] && Path.GetDirectoryName(path) is { } parent)
        {
            var window = App.WindowForElement(_host);
            if (_host is SearchResultsPage search) window?.OpenSearchResultLocation(search, parent, path);
            else window?.OpenFolderInNewTab(parent);
        }
    }
    private void Compress_Click(object sender, RoutedEventArgs e) => Schedule(CompressAsync);
    private void Copy_Click(object sender, RoutedEventArgs e) => Schedule(() => TransferAsync(false));
    private void Move_Click(object sender, RoutedEventArgs e) => Schedule(() => TransferAsync(true));

    private async Task CompressAsync()
    {
        if (FileOperationLifetime.IsBusy) { SetStatus(StringTable.Get("Files_Busy")); return; }
        using var lifetime = FileOperationLifetime.Begin();
        _transfer = new CancellationTokenSource();
        UpdateButtons();
        try
        {
            var result = await ArchiveOperationUI.RunAsync(_host, CompactMateVerb.CompressNew,
                Selected(), null, new WindowsLocalFileOperations(), _transfer.Token);
            if (result is null) return;
            if (result.WithoutUndo > 0) App.FileUndo.Clear();
            if (result.Undo is not null) App.FileUndo.Push(result.Undo);
            if (_host.IsLoaded && (result.Undo is not null || result.Completed.Count > 0)) _refresh();
            SetStatus(TransferFeedback.Format(ArchiveOperationUI.AsShelfResult(result)));
        }
        catch (OperationCanceledException) { SetStatus(StringTable.Get("Archive_Cancelled")); }
        finally { _transfer.Dispose(); _transfer = null; }
    }

    private async Task TransferAsync(bool move)
    {
        if (FileOperationLifetime.IsBusy) { SetStatus(StringTable.Get("Files_Busy")); return; }
        var sources = Selected();
        if (sources.Length == 0) return;
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, App.WindowForElement(_host)!.NativeHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        await TransferToFolderAsync(sources, folder.Path, move);
    }

    internal async Task TransferToFolderAsync(IReadOnlyList<string> sources, string destination, bool move)
    {
        if (FileOperationLifetime.IsBusy) { SetStatus(StringTable.Get("Files_Busy")); return; }
        using var lifetime = FileOperationLifetime.Begin();
        _transfer = new CancellationTokenSource();
        UpdateButtons();
        try
        {
            if (move) await App.VacateFoldersAsync(sources);
            SetStatus(StringTable.Format("Shelf_Progress", 0));
            var progress = new Progress<int>(count => SetStatus(StringTable.Format("Shelf_Progress", count)));
            var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(), sources, destination, move,
                progress, _transfer.Token, allowSameDirectoryCopy: !move, resolveConflict: FileConflictDialog.For(_host),
                byteProgress: new Progress<FileCopyProgress>(value => SetStatus(StringTable.Format("Transfer_CopyByteProgress", Path.GetFileName(value.Source),
                    (value.Transferred / 1048576d).ToString("N1"), (value.Total / 1048576d).ToString("N1")))));
            if (result.WithoutUndo > 0) App.FileUndo.Clear();
            if (result.Undo is not null)
            {
                App.FileUndo.Push(result.Undo);
            }
            if (_host.IsLoaded && (result.Undo is not null || result.Completed.Count > 0 || result.RemovedSourceDirectories.Count > 0)) _refresh();
            if (move) await FileShelfTransfer.RemoveMovedSourcesAsync(App.FileShelf, result);
            SetStatus(TransferFeedback.Format(result));
        }
        finally { _transfer.Dispose(); _transfer = null; }
    }

    internal static bool CanAccept(DataPackageView data) =>
        data.Contains(StandardDataFormats.StorageItems) && !data.Properties.ContainsKey(DragMarker);

    internal void PreviewDrop(DragEventArgs e)
    {
        var accepted = !_busy && !_dragging && CanAccept(e.DataView);
        e.AcceptedOperation = accepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (accepted)
        {
            e.DragUIOverride.IsGlyphVisible = false;
            e.DragUIOverride.Caption = StringTable.Get("Shelf_Add");
            e.DragUIOverride.IsCaptionVisible = true;
            Theming.ThemeResources.Bind(DropBorder, Border.BorderBrushProperty, "FilesMate.Selection.AccentBrush");
        }
        e.Handled = true;
    }

    private void Shelf_DragOver(object sender, DragEventArgs e) => PreviewDrop(e);
    private void Shelf_DragLeave(object sender, DragEventArgs e) => ClearDropHighlight();
    private void ClearDropHighlight() => Theming.ThemeResources.Bind(DropBorder, Border.BorderBrushProperty, "FilesMate.Divider.Brush");
    private async void Shelf_Drop(object sender, DragEventArgs e) => await ReceiveDropAsync(e);

    internal async Task ReceiveDropAsync(DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        e.Handled = true;
        try
        {
            if (_busy || _dragging || !CanAccept(e.DataView))
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }
            e.AcceptedOperation = DataPackageOperation.Copy;
            await AddDataAsync(e.DataView);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            SetStatus(error.Message);
        }
        finally { ClearDropHighlight(); deferral.Complete(); }
    }

    private void DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<ShelfItem>().Select(item => item.Path).ToArray();
        if (_busy || paths.Length == 0) { e.Cancel = true; return; }
        _dragging = true;
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
        e.Data.Properties[DragMarker] = true;
        FileDropRequest.SetSourcePaths(e.Data, paths);
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>(paths.Length);
                foreach (var path in paths)
                {
                    var directory = await Task.Run(() => Directory.Exists(path));
                    items.Add(directory ? await StorageFolder.GetFolderFromPathAsync(path)
                        : await StorageFile.GetFileFromPathAsync(path));
                }
                request.SetData(items.AsReadOnly());
            }
            catch (Exception error)
            {
                DispatcherQueue.TryEnqueue(() => SetStatus(error.Message));
            }
            finally { deferral.Complete(); }
        });
        UpdateButtons();
    }

    private async void DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragging = false;
        try
        {
            if (args.DropResult.HasFlag(DataPackageOperation.Move))
            {
                var paths = args.Items.OfType<ShelfItem>().Select(item => item.Path).ToArray();
                var missing = await Task.Run(() => paths.Where(path => !Path.Exists(path)).ToArray());
                await App.FileShelf.RemoveAsync(missing);
            }
            await ReloadAsync();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus(error.Message);
        }
        finally
        {
            UpdateButtons();
            if (_closeRequested) CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void ItemIcon_Loaded(object sender, RoutedEventArgs e)
    {
        var grid = (Grid)sender;
        if (grid.DataContext is not ShelfItem item) return;
        var directory = await Task.Run(() => Directory.Exists(item.Path));
        if (grid.IsLoaded && ReferenceEquals(grid.DataContext, item))
            ShellIconBinder.BindPath((Image)grid.Children[1], (FontIcon)grid.Children[0], item.Path, directory, 32);
    }

    private void ItemIcon_Unloaded(object sender, RoutedEventArgs e)
    {
        var grid = (Grid)sender;
        ShellIconBinder.Clear((Image)grid.Children[1], (FontIcon)grid.Children[0]);
    }

}
