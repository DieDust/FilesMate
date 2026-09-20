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
        if (sender is FrameworkElement { DataContext: RankOption rank }) { _pressedRank = rank; _dragStart = e.GetPosition(RankingPanel); }
    }
    private void Rank_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging || _pressedRank is not { } rank || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(RankingPanel) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressedRank = null;
        _dragging = true;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(RankOption), rank), DragDropEffects.Move); }
        finally { _dragging = false; ClearRankDropIndicator(); if (!IsActive) Dismiss(); }
    }
    private void Rank_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(RankOption))) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (sender is Border border) ShowRankDropIndicator(border, e.GetPosition(border).Y >= border.ActualHeight / 2);
        if (Ancestor<ScrollViewer>((DependencyObject)sender) is { } scroll)
        {
            var y = e.GetPosition(scroll).Y;
            if (y < 28) scroll.LineUp();
            else if (y > scroll.ActualHeight - 28) scroll.LineDown();
        }
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
        Rank_DragLeave(sender, e);
        if (e.Data.GetData(typeof(RankOption)) is not RankOption from || sender is not FrameworkElement { DataContext: RankOption to } element) return;
        var source = _rankItems.IndexOf(from);
        var destination = _rankItems.IndexOf(to) + (e.GetPosition(element).Y >= element.ActualHeight / 2 ? 1 : 0);
        if (source < destination) destination--;
        if (source >= 0 && destination >= 0 && destination < _rankItems.Count && source != destination)
        { _rankItems.Move(source, destination); SaveRanking(); }
        e.Handled = true;
    }
}
