using Loc = FilesMate.App.Localization.StringTable;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private bool _dragging;
    private bool _contextOpen;
    private Point _dragStart;
    private SearchRow? _pressedRow;
    private SearchRow? _pressedApplication;
    private RankOption? _pressedRank;
    private bool _rankDropAccepted;
    private long _rankFocusGraceUntil;
    private long _lastRankAutoScroll;
    private bool _collapseSelectionOnUp;
    private ContextMenu? _resultMenu;

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private static Border? FindRankRow(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Name: "RankingRow" } row) return row;
            if (FindRankRow(child) is { } nested) return nested;
        }
        return null;
    }
    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
    private SearchRow[] SelectedRows() => Results.SelectedItems.Cast<SearchRow>().ToArray();
    private static bool Exists(SearchRow row) => row.IsApplication || File.Exists(row.Path) || Directory.Exists(row.Path);
    private void ActionError(string text) { StatusText.Text = text; StatusText.Visibility = Visibility.Visible; }

    internal static DataObject FileTransferData(string[] paths, bool cut = false)
    {
        var data = new DataObject();
        var files = new StringCollection();
        files.AddRange(paths);
        data.SetFileDropList(files);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
        return data;
    }
    private void CopyFiles(bool cut = false)
    {
        if (_pending) return;
        var rows = SelectedRows();
        if (rows.Length == 0) return;
        if (rows.Any(row => row.IsApplication)) { ActionError(Loc.Get("Search_CopyAppHint")); return; }
        if (rows.Any(row => !Exists(row))) { ActionError(Loc.Get("Search_StaleFiles")); return; }
        try { Clipboard.SetDataObject(FileTransferData(rows.Select(row => row.Path).ToArray(), cut), true); CountLabel.Text = Loc.Format(cut ? "Clipboard_CutCount" : "Clipboard_CopyCount", rows.Length); }
        catch (ExternalException) { ActionError(Loc.Get("Clipboard_Busy")); }
    }
    private void Results_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedRow = null;
        _pressedApplication = null;
        _collapseSelectionOnUp = false;
        if (e.ChangedButton == MouseButton.Right) ClearPreview();
        if (_pending || Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        var item = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not SearchRow row) { if (e.ChangedButton == MouseButton.Right) Results.UnselectAll(); return; }
        if (e.ChangedButton == MouseButton.Right)
        {
            _suppressPreviewSelection = true;
            try
            {
                if (!item.IsSelected) { Results.SelectedItems.Clear(); item.IsSelected = true; }
                item.Focus();
            }
            finally { _suppressPreviewSelection = false; }
        }
        if (e.ChangedButton != MouseButton.Left) return;
        _pressedRow = row.IsApplication ? null : row;
        _pressedApplication = row.IsApplication && Keyboard.Modifiers == ModifierKeys.None ? row : null;
        _dragStart = e.GetPosition(Results);
        // Preserve the whole selection when dragging one of the selected rows.
        if (item.IsSelected && Keyboard.Modifiers == ModifierKeys.None && e.ClickCount == 1)
        { _collapseSelectionOnUp = true; e.Handled = true; }
    }
    private void Results_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var application = _pressedApplication;
        _pressedApplication = null;
        if (_collapseSelectionOnUp && _pressedRow is { } row && e.ChangedButton == MouseButton.Left)
        { Results.SelectedItems.Clear(); Results.SelectedItems.Add(row); }
        _pressedRow = null;
        _collapseSelectionOnUp = false;
        // Application entries are launcher actions. Ordinary files retain their
        // selection/double-click behavior, and modifier clicks never launch.
        var delta = e.GetPosition(Results) - _dragStart;
        if (ShouldLaunchApplicationClick(application, Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext,
            e.ChangedButton, Keyboard.Modifiers, delta))
        {
            Results.SelectedItem = application;
            OpenSelected(false);
            e.Handled = true;
        }
    }

    private static bool ShouldLaunchApplicationClick(SearchRow? pressed, object? released,
        MouseButton button, ModifierKeys modifiers, Vector movement)
        => pressed is { IsApplication: true } && ReferenceEquals(pressed, released)
            && button == MouseButton.Left && modifiers == ModifierKeys.None
            && Math.Abs(movement.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(movement.Y) < SystemParameters.MinimumVerticalDragDistance;

    private void Results_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pending || _dragging || _pressedRow is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(Results) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var selected = SelectedRows();
        if (selected.Any(row => row.IsApplication)) { _pressedRow = null; return; }
        if (selected.Any(row => !Exists(row))) { _pressedRow = null; ActionError(Loc.Get("Search_StaleFiles")); return; }
        var paths = selected.Select(row => row.Path).ToArray();
        _pressedRow = null;
        _collapseSelectionOnUp = false;
        if (paths.Length == 0) return;
        _dragging = true;
        ClearPreview();
        try { DragDrop.DoDragDrop(Results, FileTransferData(paths), DragDropEffects.Copy); }
        catch (ExternalException error) { ActionError(Loc.Get("Drag_FailedPrefix") + error.Message); }
        finally { _dragging = false; if (!IsActive) Dismiss(); }
    }
    private void Results_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        OpenResultMenu(e.CursorLeft < 0);
    }
    private void ShowProperties()
    {
        if (_pending || SelectedRows() is not [var row] || row.FilePath is null) return;
        try
        {
            new FilesMate.Platform.Windows.Operations.WindowsLocalFileOperations().ShowProperties(row.FilePath);
        }
        catch (Exception error) when (error is IOException or Win32Exception) { ActionError(error.Message); }
    }

    private void Rank_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedRank = null;
        if (Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is Border { DataContext: RankOption rank } row)
        {
            _pressedRank = rank;
            _dragStart = e.GetPosition(RankingScroll);
            row.Focus();
            row.CaptureMouse();
        }
    }
    private void Rank_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _pressedRank = null;
        if (sender is UIElement { IsMouseCaptured: true } row) row.ReleaseMouseCapture();
    }
    private void Rank_LostCapture(object sender, MouseEventArgs e) => _pressedRank = null;
    private void Rank_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Border { DataContext: RankOption rank } || Keyboard.Modifiers != ModifierKeys.Alt
            || e.Key is not (Key.Up or Key.Down)) return;
        e.Handled = true;
        MoveRank(rank, e.Key == Key.Up ? -1 : 1);
    }
    private void Rank_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging || _pressedRank is not { } rank || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(RankingScroll) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressedRank = null;
        if (sender is UIElement { IsMouseCaptured: true } row) row.ReleaseMouseCapture();
        _dragging = true;
        _rankDropAccepted = false;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(RankOption), rank), DragDropEffects.Move); }
        catch (ExternalException error) { RankingStatus.Text = Loc.Get("Order_SaveFailed") + error.Message; }
        finally
        {
            _dragging = false;
            ClearRankDropIndicator();
            if (_rankDropAccepted)
            {
                _rankFocusGraceUntil = Environment.TickCount64 + 350;
                Dispatcher.BeginInvoke(new Action(() => { if (IsVisible && RankingPanel.IsVisible) Activate(); }), DispatcherPriority.Background);
                RefreshSharedRanking();
            }
            else if (!IsActive) Dismiss();
        }
    }
    private void Rank_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(RankOption))) { e.Effects = DragDropEffects.None; return; }
        if (!TryRankDropPosition(sender, e, out var row, out var after)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        ShowRankDropIndicator(row, after);
        if (Environment.TickCount64 - _lastRankAutoScroll >= 80)
        {
            var y = e.GetPosition(RankingScroll).Y;
            if (y < 28) RankingScroll.LineUp();
            else if (y > RankingScroll.ActualHeight - 28) RankingScroll.LineDown();
            _lastRankAutoScroll = Environment.TickCount64;
        }
    }
    private bool TryRankDropPosition(object sender, DragEventArgs e, out Border row, out bool after)
    {
        if (sender is Border { DataContext: RankOption } direct)
        {
            row = direct;
            after = e.GetPosition(direct).Y >= direct.ActualHeight / 2;
            return true;
        }
        var y = e.GetPosition(RankingScroll).Y;
        Border? last = null;
        for (var index = 0; index < _rankItems.Count; index++)
        {
            if (RankingItems.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container
                || FindRankRow(container) is not { } candidate) continue;
            last = candidate;
            if (y >= candidate.TranslatePoint(new Point(0, candidate.ActualHeight / 2), RankingScroll).Y) continue;
            row = candidate;
            after = false;
            return true;
        }
        row = last!;
        after = true;
        return last is not null;
    }
    private Border? _rankDropIndicator;
    private void ShowRankDropIndicator(Border row, bool after)
    {
        ClearRankDropIndicator();
        if (row.Child is not Grid grid) return;
        _rankDropIndicator = grid.Children.OfType<Border>().FirstOrDefault(b => b.Name == "RankDropIndicator");
        if (_rankDropIndicator is not { } indicator) return;
        indicator.VerticalAlignment = after ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        indicator.Visibility = Visibility.Visible;
    }
    private void ClearRankDropIndicator()
    {
        if (_rankDropIndicator is { } indicator) indicator.Visibility = Visibility.Collapsed;
        _rankDropIndicator = null;
    }
    private void Rank_DragLeave(object sender, DragEventArgs e) => ClearRankDropIndicator();
    private void Rank_Drop(object sender, DragEventArgs e)
    {
        var hasTarget = TryRankDropPosition(sender, e, out var row, out var after);
        ClearRankDropIndicator();
        if (!hasTarget || e.Data.GetData(typeof(RankOption)) is not RankOption from || row.DataContext is not RankOption to) return;
        _rankDropAccepted = true;
        var source = _rankItems.IndexOf(from);
        var destination = _rankItems.IndexOf(to) + (after ? 1 : 0);
        if (source < destination) destination--;
        if (source >= 0 && destination >= 0 && destination < _rankItems.Count && source != destination)
        { _rankItems.Move(source, destination); SaveRanking(); }
        e.Handled = true;
    }
}
