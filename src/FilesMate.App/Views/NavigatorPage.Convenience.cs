using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.App.Shortcuts;
using FilesMate.App.Theming;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private ContentDialog? _commandDialog;
    private Border? _operationNotice;
    private TextBlock? _operationText;
    private Button? _operationUndo;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _operationNoticeTimer;
    private FileUndoRecord? _noticeRecord;
    private bool _restoringClosedTab;

    private void InitializeConvenience()
    {
        Loaded += (_, _) =>
        {
            ShowBackupRemnantNotice();
            App.FileUndo.Recorded -= OperationRecorded;
            App.FileUndo.Recorded += OperationRecorded;
            App.FileUndo.Changed -= UndoHistoryChanged;
            App.FileUndo.Changed += UndoHistoryChanged;
        };
        Unloaded += (_, _) =>
        {
            CloseQuickPreview();
            App.FileUndo.Recorded -= OperationRecorded;
            App.FileUndo.Changed -= UndoHistoryChanged;
            _operationNoticeTimer?.Stop();
            _noticeRecord = null;
            if (_operationNotice is not null) _operationNotice.Visibility = Visibility.Collapsed;
            _commandDialog?.Hide();
        };
    }

    private void ConfigureConvenienceSurface(FileDetailsSurface surface, bool right)
    {
        surface.OtherPanePath = () => OtherPaneDestination(right);
        surface.RenameRequested = _fileActions.RenamePathAsync;
        surface.QuickPreviewRequested += (_, _) =>
        {
            ActivateRight(right);
            ToggleQuickPreview();
        };
    }

    private string? OtherPaneDestination(bool right)
    {
        var path = _dualPane ? (right ? _leftVm : _rightVm)?.AddressText : null;
        var pane = right ? _leftVm : _rightVm;
        return path is not null && (Directory.Exists(path) || pane?.IsPortableDevice == true && pane.CanReceiveFiles) ? path : null;
    }

    private async Task TransferToOtherPaneAsync(bool move)
    {
        var target = OtherPaneDestination(_rightActive);
        var sources = SelectedPaths().ToArray();
        if (target is null || sources.Length == 0) return;
        var deviceTransfer = ViewModel.IsPortableDevice || FilesMate.Platform.Windows.Shell.PortableDeviceLocation.TryParse(target, out _);
        if (deviceTransfer && move) { ViewModel.ReportUserError(Loc.Get("Device_CopyOnly")); return; }
        if (!deviceTransfer && FileDropPolicy.FilterSources(sources, target, move, false).Count != sources.Length)
        {
            ViewModel.ReportUserError(Loc.Get("Files_InvalidDestination"));
            return;
        }
        var dialog = new ContentDialog { Title = move ? Loc.Get("Files_MoveOtherPane") : Loc.Get("Files_CopyOtherPane"),
            Content = new TextBlock { Text = Loc.Format("Files_DestinationSummary", sources.Length, target), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = move ? Loc.Get("Move") : Loc.Get("Command_Copy"), CloseButtonText = Loc.Get("Cancel"), XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary };
        ContentDialogTheme.Apply(dialog, this);
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await _fileActions.DropAsync(sources, target, move ? DataPackageOperation.Move : DataPackageOperation.Copy);
        }
        catch (Exception error) { if (!_disposed) ViewModel.ReportUserError(error.Message); }
    }

    private void OperationRecorded(object? sender, FileUndoRecord record) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!IsLoaded || _disposed || !ReferenceEquals(App.FileUndo.Latest, record)) return;
        if (_transferResultNotice is { IsOpen: true })
        {
            if (ReferenceEquals(_transferResultRecord, record)) return;
            _transferResultNotice.IsOpen = false;
        }
        if (_operationNotice is null)
        {
            _operationText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 };
            _operationUndo = new Button { Content = Loc.Get("Undo"), MinWidth = 64 };
            _operationUndo.Click += (_, _) =>
            {
                if (_noticeRecord is not null && ReferenceEquals(App.FileUndo.Latest, _noticeRecord)) _fileActions.Undo();
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(_operationText);
            content.Children.Add(_operationUndo);
            _operationNotice = new Border { Child = content, Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(16, 0, 16, 40),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FilesMate.Menu.BackgroundBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FilesMate.Card.BorderBrush"], BorderThickness = new Thickness(1) };
            Canvas.SetZIndex(_operationNotice, 30);
            ShellRoot.Children.Add(_operationNotice);
            _operationNoticeTimer = DispatcherQueue.CreateTimer();
            _operationNoticeTimer.Interval = TimeSpan.FromSeconds(8);
            _operationNoticeTimer.Tick += (_, _) => { _operationNoticeTimer.Stop(); _operationNotice.Visibility = Visibility.Collapsed; _noticeRecord = null; };
        }
        _noticeRecord = record;
        var count = record.Pairs.Count > 0 ? record.Pairs.Count : record.Paths.Count + record.CreatedDirectories.Count;
        count += record.Replacements.Count;
        _operationText!.Text = record.Kind switch
        {
            FileUndoKind.Grouped => Loc.Format("Files_GroupedCount", count),
            FileUndoKind.Recycled => Loc.Format("Files_RecycledCount", count),
            FileUndoKind.Relocated => Loc.Format("Files_RenamedMovedCount", count),
            FileUndoKind.Merged => Loc.Get("Files_Merged"),
            _ => Loc.Format("Files_CreatedCopiedCount", count)
        };
        _operationNotice.Visibility = Visibility.Visible;
        _operationNoticeTimer!.Stop();
        _operationNoticeTimer.Start();
    });

    private void UndoHistoryChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_transferResultNotice?.ActionButton is Button transferUndo)
            transferUndo.IsEnabled = _transferResultRecord is not null && ReferenceEquals(_transferResultRecord, App.FileUndo.Latest);
        if (!ReferenceEquals(_noticeRecord, App.FileUndo.Latest) && _operationNotice is not null)
        {
            _operationNotice.Visibility = Visibility.Collapsed;
            _operationNoticeTimer?.Stop();
            _noticeRecord = null;
        }
    });

    private void CommandPaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (App.IsShortcutCaptureActive) return;
        args.Handled = true;
        _ = ShowCommandPaletteAsync();
    }

    private sealed record PaletteAction(string Label, string Hint, bool Enabled, Action Invoke)
    {
        public override string ToString() => Label + (Hint.Length > 0 ? "    " + Hint : "");
    }

    private async Task ShowCommandPaletteAsync()
    {
        if (_commandDialog is not null || _disposed || !IsLoaded) return;
        var folder = ViewModel.AddressText;
        var hasFolder = ViewModel.IsPortableDevice || Directory.Exists(folder);
        var selected = ActiveSurface.Selection.Count;
        var actions = new List<PaletteAction>();
        void Add(string label, string hint, bool enabled, Action run) => actions.Add(new(label, hint, enabled, run));
        void FileAction(AppCommandId id, bool enabled, string hint = "") =>
            Add(CommandCatalog.Resolve(id, CommandContext.ForToolbar(selected)).Label, hint,
                enabled && (!ViewModel.IsPortableDevice || CommandCatalog.CanExecute(id, DeviceCommandContext())), () => RunFileCommand(id));
        Add(Loc.Get("Action_ReopenTab"), App.Shortcuts[ShortcutAction.ReopenTab].DisplayText, true, () => App.CurrentWindow?.ReopenClosedTab());
        FileAction(AppCommandId.NewFolder, hasFolder, App.Shortcuts[ShortcutAction.NewFolder].DisplayText);
        FileAction(AppCommandId.NewFile, hasFolder);
        FileAction(AppCommandId.NewFolderWithSelection, hasFolder && selected > 0);
        FileAction(selected > 1 ? AppCommandId.BatchRename : AppCommandId.Rename, selected > 0, App.Shortcuts[ShortcutAction.Rename].DisplayText);
        FileAction(AppCommandId.OpenInTerminal, hasFolder, App.Shortcuts[ShortcutAction.OpenTerminal].DisplayText);
        Add(Loc.Get("Action_CopySelectedPaths"), App.Shortcuts[ShortcutAction.CopyPath].DisplayText, selected > 0 && !ViewModel.IsPortableDevice, () => CopySelectedPaths());
        Add(Loc.Get("Action_CopyFolderPath"), "", hasFolder && !ViewModel.IsPortableDevice, () => { var data = new DataPackage(); data.SetText(folder); Clipboard.SetContent(data); });
        FileAction(AppCommandId.Copy, selected > 0, App.Shortcuts[ShortcutAction.Copy].DisplayText);
        FileAction(AppCommandId.Cut, selected > 0, App.Shortcuts[ShortcutAction.Cut].DisplayText);
        FileAction(AppCommandId.Paste, hasFolder && PaneFileActions.ClipboardHasFiles(), App.Shortcuts[ShortcutAction.Paste].DisplayText);
        FileAction(AppCommandId.CopyToOtherPane, selected > 0 && OtherPaneDestination(_rightActive) is not null);
        FileAction(AppCommandId.MoveToOtherPane, selected > 0 && OtherPaneDestination(_rightActive) is not null);
        Add(Loc.Get("SelectSameType"), "", selected > 0, ActiveSurface.SelectSameType);
        Add(Loc.Get("InvertSelection"), "", ViewModel.ItemCount > 0, ActiveSurface.InvertSelection);
        Add(Loc.Get("Action_UndoFile"), App.Shortcuts[ShortcutAction.Undo].DisplayText, App.FileUndo.CanUndo, _fileActions.Undo);
        Add(Loc.Get("Action_RedoFile"), App.Shortcuts[ShortcutAction.Redo].DisplayText, App.FileUndo.CanRedo, _fileActions.Redo);
        Add(_quickPreview is null ? Loc.Get("Action_QuickPreview") : Loc.Get("Action_CloseQuickPreview"), "Space", PrimarySelectedPath() is not null, ToggleQuickPreview);
        Add(_previewVisible ? Loc.Get("Action_ClosePreviewPane") : Loc.Get("Action_OpenPreviewPane"), App.Shortcuts[ShortcutAction.Preview].DisplayText, true, () => SetPreviewVisible(!_previewVisible));
        Add(_dualPane ? Loc.Get("Action_CloseDualPane") : Loc.Get("Action_OpenDualPane"), "Ctrl+Shift+S", true, ToggleDualPane);
        Add(Loc.Get("Action_ToggleHidden"), "", true, () => _ = App.SetExplorerPreferencesAsync(App.ExplorerPreferences with { ShowHiddenFiles = !App.ExplorerPreferences.ShowHiddenFiles }));
        Add(Loc.Get("Action_ToggleFolderSizes"), "", !ViewModel.IsPortableDevice, ToggleFolderSizes);
        Add(Loc.Get("DetailsView"), "", true, () => ActiveSurface.SetLayout(FileLayoutKind.Details));
        Add(Loc.Get("Action_IconView"), "", true, () => ActiveSurface.SetLayout(FileLayoutKind.Grid));
        foreach (var column in FilesMate.App.Models.DetailsColumn.Defaults())
            Add(Loc.Format("Action_SortColumn", column.Title), "", true, () => ViewModel.SetSortColumn(column.Sort));
        Add(Loc.Get("Action_RefreshFolder"), App.Shortcuts[ShortcutAction.Refresh].DisplayText, ViewModel.CanRefresh, RefreshFilePanes);
        Add(Loc.Get("Action_OpenSettings"), "", true, () => App.CurrentWindow?.OpenSettings("general"));
        var search = new TextBox { PlaceholderText = Loc.Get("Action_SearchHint"), MinWidth = 320 };
        var list = new ListView { MaxHeight = 360, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
        var empty = new TextBlock { Text = Loc.Get("Action_NoMatch"), Visibility = Visibility.Collapsed, Margin = new Thickness(8) };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(search); panel.Children.Add(list); panel.Children.Add(empty);
        var dialog = new ContentDialog { Title = Loc.Get("CommandPalette"), Content = panel, CloseButtonText = Loc.Get("GlassEffectOff"), XamlRoot = XamlRoot };
        ContentDialogTheme.Apply(dialog, this);
        PaletteAction? chosen = null;
        void Update()
        {
            list.Items.Clear();
            foreach (var action in actions.Where(a => a.ToString().Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                list.Items.Add(new ListViewItem { Content = action.ToString(), Tag = action, IsEnabled = action.Enabled });
            list.SelectedItem = list.Items.OfType<ListViewItem>().FirstOrDefault(i => i.IsEnabled);
            empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void Choose()
        {
            if (list.SelectedItem is ListViewItem { Tag: PaletteAction { Enabled: true } action }) { chosen = action; dialog.Hide(); }
        }
        search.TextChanged += (_, _) => Update();
        search.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { Choose(); e.Handled = true; }
            else if (e.Key == VirtualKey.Down) { list.Focus(FocusState.Programmatic); e.Handled = true; }
        };
        list.ItemClick += (_, e) => { if (e.ClickedItem is ListViewItem { Tag: PaletteAction { Enabled: true } action }) { chosen = action; dialog.Hide(); } };
        list.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Choose(); e.Handled = true; } };
        dialog.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        _commandDialog = dialog;
        try { Update(); await dialog.ShowAsync(); }
        catch (Exception error) { if (!_disposed) ViewModel.ReportUserError(error.Message); }
        finally { _commandDialog = null; }
        if (chosen is not null && IsLoaded && !_disposed && folder == ViewModel.AddressText)
        {
            ActiveSurface.Focus(FocusState.Programmatic);
            await Task.Yield();
            try { chosen.Invoke(); }
            catch (Exception error) { if (!_disposed) ViewModel.ReportUserError(error.Message); }
        }
    }
    public ClosedTabState CaptureClosedTab() => new(
        CapturePane(_leftVm, FileSurface),
        _dualPane && _rightVm is not null && _rightSurface is not null ? CapturePane(_rightVm, _rightSurface) : null,
        _rightActive, _previewVisible);

    public bool CanHibernate => !_disposed && !_restoringClosedTab && !FileOperationLifetime.IsBusy && !PaneFileActions.IsBusy
        && !_leftVm.IsLoading && _rightVm?.IsLoading != true && !FileSurface.IsRenaming && _rightSurface?.IsRenaming != true
        && !Omni.IsEditing && !Omni.IsSearchOpen && _commandDialog is null
        && _leftVm.TagFilterLabel is null && _rightVm?.TagFilterLabel is null;

    internal bool CanInstallUpdate => !FileOperationLifetime.IsBusy && !PaneFileActions.IsBusy
        && !FileSurface.IsRenaming && _rightSurface?.IsRenaming != true && _commandDialog is null;

    private static ClosedPaneState CapturePane(PaneViewModel vm, FileDetailsSurface surface) =>
        new(vm.AddressText, new FolderViewSettings(surface.LayoutKind == FileLayoutKind.Details,
            surface.GridPreset.Slot, vm.Sort, surface.GetColumns()), surface.ScrollOffset,
            surface.SelectedPaths().Select(path => Path.GetFileName(path)).ToArray(), vm.FilterQuery, vm.Navigation.CaptureHistory());

    public async Task RestoreClosedTabAsync(ClosedTabState state)
    {
        _restoringClosedTab = true;
        try
        {
            if (!await Ready(_leftVm, state.Left.Path)) return;
            ApplyFolderView(_leftVm, state.Left.View);
            await RestorePane(_leftVm, FileSurface, state.Left);
            if (state.Right is { } right)
            {
                SetDualPane(true, persist: false);
                _rightVm!.Navigate(right.Path);
                if (!await Ready(_rightVm, right.Path)) return;
                ApplyFolderView(_rightVm, right.View);
                await RestorePane(_rightVm, _rightSurface!, right);
                ActivateRight(state.RightActive);
            }
            if (!_disposed) SetPreviewVisible(state.PreviewVisible);
        }
        catch (Exception error) { App.LogFailure("RestoreTab", error); if (!_disposed) ViewModel.ReportUserError(error.Message); }
        finally { _restoringClosedTab = false; }

        async Task RestorePane(PaneViewModel vm, FileDetailsSurface surface, ClosedPaneState pane)
        {
            if (!string.Equals(vm.AddressText, pane.Path, StringComparison.OrdinalIgnoreCase)) return;
            var generation = vm.Navigation.CurrentGeneration;
            if (pane.History is not null) vm.RestoreNavigationHistory(pane.History);
            if (vm.FilterQuery != pane.FilterQuery) vm.SetFilterQuery(pane.FilterQuery);
            for (var i = 0; i < 200 && !_disposed; i++)
            {
                await Task.Delay(50);
                if (HomeLocation.IsHome(pane.Path) || !vm.IsLoading && vm.ViewIndex is { } index
                    && index.Sort == vm.Sort && index.Filter.Query == pane.FilterQuery && surface.IsBoundTo(vm.Store, index)) break;
            }
            if (_disposed || generation != vm.Navigation.CurrentGeneration) return;
            surface.RestoreSelectedNames(pane.SelectedNames ?? []);
            surface.RestoreScrollOffset(pane.ScrollOffset);
        }

        async Task<bool> Ready(PaneViewModel vm, string path)
        {
            for (var i = 0; i < 200 && !_disposed; i++)
            {
                await Task.Delay(50);
                if (IsLoaded && !vm.IsLoading && !_restoringViews.ContainsKey(vm)
                    && string.Equals(vm.AddressText, path, StringComparison.OrdinalIgnoreCase)
                    && (HomeLocation.IsHome(path) || vm.ViewIndex?.Generation == vm.Navigation.CurrentGeneration)) return true;
            }
            return false;
        }
    }
}
