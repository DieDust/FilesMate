using FilesMate.App.Commands;
using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Icons;
using FilesMate.App.Localization;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FilesMate.App.Views;

public sealed partial class NavigatorPage
{
    private readonly Dictionary<PaneViewModel, long> _viewRevisions = [];
    private readonly HashSet<PaneViewModel> _applyingView = [];
    private readonly Dictionary<PaneViewModel, long> _restoringViews = [];
    private FileShelfPanel? _shelfPanel;

    private void FolderViewScopeChanged(object? sender, EventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            _ = RestoreFolderViewAsync(_leftVm);
            if (_rightVm is not null) _ = RestoreFolderViewAsync(_rightVm);
        });
    }

    private async Task RestoreFolderViewAsync(PaneViewModel vm)
    {
        var path = vm.AddressText;
        var revision = _viewRevisions.GetValueOrDefault(vm) + 1;
        _viewRevisions[vm] = revision;
        _restoringViews[vm] = revision;
        try
        {
            var settings = await App.FolderCustomizations.GetAsync(path);
            await Task.Yield();
            if (_disposed || _viewRevisions.GetValueOrDefault(vm) != revision
                || !string.Equals(vm.AddressText, path, StringComparison.OrdinalIgnoreCase))
                return;
            ApplyFolderView(vm, settings.View);
        }
        finally
        {
            if (_restoringViews.GetValueOrDefault(vm) == revision)
                _restoringViews.Remove(vm);
        }

        TryApplyPendingSelection(vm);
    }

    private void ApplyFolderView(PaneViewModel vm, FolderViewSettings? view)
    {
        _applyingView.Add(vm);
        try
        {
            var surface = SurfaceOf(vm);
            surface.SetColumns(view?.Columns);
            surface.SetGridSize(GridSizePreset.All.FirstOrDefault(p => p.Slot == view?.GridSlot, GridSizePreset.Default));
            surface.SetLayout(view is null ? ToFileLayout(App.ExplorerPreferences.DefaultView)
                : view.Details ? FileLayoutKind.Details : FileLayoutKind.Grid);
            vm.RestoreSort((view?.Sort ?? EntrySort.Name) with { MixChineseAndLatin = App.ExplorerPreferences.MixChineseAndLatin });
        }
        finally { _applyingView.Remove(vm); }
        if (ReferenceEquals(vm, ViewModel))
            RefreshLayoutChrome();
    }

    private void PersistFolderView(PaneViewModel vm)
    {
        if (_disposed || _applyingView.Contains(vm) || !_viewRevisions.ContainsKey(vm)
            || FolderCustomizationStore.Key(vm.AddressText) is null)
            return;
        _viewRevisions[vm]++;
        var surface = SurfaceOf(vm);
        _ = SaveFolderViewAsync(vm.AddressText,
            new(surface.LayoutKind == FileLayoutKind.Details, surface.GridPreset.Slot, vm.Sort, surface.GetColumns()));
    }

    private async Task SaveFolderViewAsync(string path, FolderViewSettings view)
    {
        try { await App.FolderCustomizations.SetViewAsync(path, view); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!_disposed) ViewModel.ReportUserError(error.Message);
        }
    }

    private void FolderCoversChanged(object? sender, EventArgs args)
    {
        if (_disposed) return;
        FileSurface.RebindVisibleEntries();
        _rightSurface?.RebindVisibleEntries();
    }

    private async Task RunCustomizationCommandAsync(AppCommandId id)
    {
        var vm = ViewModel;
        var selected = SelectedPaths();
        var folder = selected.Count == 1 ? selected[0] : vm.AddressText;
        try
        {
            if (id == AppCommandId.AddToShelf)
            {
                if (selected.Count > 0) await App.FileShelf.AddAsync(selected);
                await ShowShelfAsync();
                return;
            }
            if (id == AppCommandId.ShowShelf)
            {
                if (ShelfCard.Visibility == Visibility.Visible)
                    _shelfPanel?.RequestClose();
                else
                    await ShowShelfAsync();
                return;
            }
            if (id == AppCommandId.ResetFolderView)
            {
                await App.FolderCustomizations.SetViewAsync(vm.AddressText, null);
                if (!_disposed) await RestoreFolderViewAsync(vm);
                return;
            }
            if (selected.Count > 1 || !await Task.Run(() => Directory.Exists(folder))) return;
            string? cover = null;
            if (id == AppCommandId.ChooseFolderCover)
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".heic",
                    ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpeg", ".mpg" })
                    picker.FileTypeFilter.Add(extension);
                InitializeWithWindow.Initialize(picker, App.WindowForElement(this)!.NativeHandle);
                var file = await picker.PickSingleFileAsync();
                if (file is null || _disposed) return;
                cover = file.Path;
                if (!string.Equals(FolderCustomizationStore.Key(Path.GetDirectoryName(cover)),
                    FolderCustomizationStore.Key(folder), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(StringTable.Get("Cover_DirectChild"));
            }
            await App.FolderCustomizations.SetCoverAsync(folder, cover);
            FolderPreviewBinder.ClearCache();
            App.NotifyFolderCoversChanged();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            if (!_disposed) vm.ReportUserError(error.Message);
        }
    }

    private async Task ShowShelfAsync()
    {
        if (_disposed || XamlRoot is null) return;
        if (_shelfPanel is null)
        {
            _shelfPanel = new FileShelfPanel(this, RefreshFilePanes);
            _shelfPanel.CloseRequested += (_, _) =>
            {
                _shelfPanel.IsOpen = false;
                ShelfCard.Visibility = Visibility.Collapsed;
            };
            ShelfContent.Content = _shelfPanel;
        }
        ShelfCard.Visibility = Visibility.Visible;
        _shelfPanel.IsOpen = true;
        PositionShelf();
        await _shelfPanel.ReloadAsync();
    }

    private void PositionShelf()
    {
        if (_disposed || ShelfCard.Visibility != Visibility.Visible || !Commands.IsLoaded) return;
        var anchor = Commands.ShelfAnchor;
        var point = anchor.TransformToVisual(ShellRoot).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight));
        var width = Math.Min(360, Math.Max(280, ShellRoot.ActualWidth - 24));
        ShelfCard.Width = width;
        ShelfCard.MaxHeight = Math.Max(180, ShellRoot.ActualHeight - point.Y - 20);
        ShelfCard.Margin = new Thickness(Math.Clamp(point.X + anchor.ActualWidth - width, 12,
            Math.Max(12, ShellRoot.ActualWidth - width - 12)), point.Y + 8, 0, 0);
    }

    private void ShelfDragOver(object? sender, DragEventArgs e)
    {
        if (!FileShelfPanel.CanAccept(e.DataView))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = StringTable.Get("Shelf_Add");
        e.Handled = true;
        if (ShelfCard.Visibility == Visibility.Visible) return;
        // OLE drag loops can defer dispatcher timers until the pointer is released.
        _ = ShowShelfAsync();
    }

    private async void ShelfDrop(object? sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        e.Handled = true;
        try
        {
            await ShowShelfAsync();
            if (_shelfPanel is not null) await _shelfPanel.ReceiveDropAsync(e);
        }
        finally { deferral.Complete(); }
    }

    private async Task TransferShelfDropAsync(FileDropRequest request, string destination, PaneViewModel vm)
    {
        if (FileOperationLifetime.IsBusy) { vm.ReportUserError(StringTable.Get("Files_Busy")); return; }
        using var lifetime = FileOperationLifetime.Begin();
        try
        {
            var move = request.Operation == DataPackageOperation.Move;
            if (move) await App.VacateFoldersAsync(request.Paths);
            var result = await FileShelfTransfer.RunAsync(new WindowsLocalFileOperations(), request.Paths, destination, move,
                allowSameDirectoryCopy: request.AllowSameDirectoryCopy, resolveConflict: FileConflictDialog.For(this));
            if (result.WithoutUndo > 0) App.FileUndo.Clear();
            if (result.Undo is not null)
            {
                App.FileUndo.Push(result.Undo);
                if (!_disposed) RefreshFilePanes();
            }
            ShowTransferFeedback(result);
            if (TransferFeedback.NeedsAttention(result) && !_disposed)
                vm.ReportUserError(TransferFeedback.Format(result));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!_disposed) vm.ReportUserError(error.Message);
        }
    }
}
