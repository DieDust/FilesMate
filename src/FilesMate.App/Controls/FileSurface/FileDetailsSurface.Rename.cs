using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private TextBox? _renameEditor;
    private TextBlock? _renameLabel;
    private FrameworkElement? _renameRow;
    private string? _renamePath;
    private int _renameSequence;
    public bool IsRenaming => _renameEditor is not null;
    public Func<string, string, Task<string?>>? RenameRequested { get; set; }

    public async void BeginInlineRename()
    {
        if (IsRenaming || RenameRequested is null || !IsFolderWritable || _selection.Count != 1) return;
        var sequence = ++_renameSequence;
        var targetId = _selection.PrimaryId;
        var generation = _generation;
        ScrollPrimaryIntoView();
        for (var attempt = 0; attempt < 20 && IsLoaded && sequence == _renameSequence; attempt++)
        {
            await Task.Delay(30);
            if (!IsLoaded || sequence != _renameSequence || generation != _generation
                || targetId != _selection.PrimaryId) return;
            if (_selection.PrimaryId is not int id || !TryGetPrimary(out var entry)) return;
            var row = (FrameworkElement?)_realized.FirstOrDefault(r => r.EntryId == id)
                ?? _tiles.FirstOrDefault(t => t.EntryId == id);
            if (row?.FindName("NameText") is not TextBlock label || label.Parent is not Grid parent) continue;
            _renamePath = ResolvePath?.Invoke(entry);
            if (string.IsNullOrEmpty(_renamePath)) return;
            _renameLabel = label;
            _renameRow = row;
            var editor = new TextBox { Text = entry.Name, MinWidth = 0, MinHeight = 0,
                Padding = new Thickness(2, 0, 2, 0), FontSize = label.FontSize,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(editor, Loc.Get("FileName"));
            Grid.SetColumn(editor, Grid.GetColumn(label));
            Grid.SetRow(editor, Grid.GetRow(label));
            Canvas.SetZIndex(editor, 10);
            _renameEditor = editor;
            label.Visibility = Visibility.Collapsed;
            parent.Children.Add(editor);
            editor.PreviewKeyDown += RenameEditor_KeyDown;
            editor.LostFocus += RenameEditor_LostFocus;
            editor.Focus(FocusState.Programmatic);
            editor.Select(0, FileNameRules.StemLength(entry.Name, entry.Kind == EntryKind.Directory));
            return;
        }
    }

    private void RenameEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { CancelInlineRename(); Focus(FocusState.Programmatic); e.Handled = true; }
        else if (e.Key is VirtualKey.Enter or VirtualKey.Tab)
        {
            e.Handled = true;
            _ = CommitInlineRenameAsync(e.Key == VirtualKey.Tab ? (IsModifier(VirtualKey.Shift) ? -1 : 1) : 0);
        }
    }
    private void RenameEditor_LostFocus(object sender, RoutedEventArgs e) => _ = CommitInlineRenameAsync(0);

    private async Task CommitInlineRenameAsync(int direction)
    {
        if (_renameEditor is not { } editor || _renamePath is not { } path || RenameRequested is null) return;
        var name = editor.Text;
        var folder = ResolveFolder?.Invoke();
        string? nextName = null;
        if (direction != 0 && _items.Store is not null && _items.Index is not null)
        {
            var next = _selection.ViewIndexOfPrimary(_items.Store, _items.Index) + direction;
            if (next >= 0 && next < _items.Count && _items.TryGetEntry(next, out var entry)) nextName = entry.Name;
        }
        CancelInlineRename();
        var sequence = _renameSequence;
        var renamedPath = await RenameRequested(path, name);
        var succeeded = renamedPath is not null;
        if (!IsLoaded || sequence != _renameSequence || !string.Equals(folder, ResolveFolder?.Invoke(), StringComparison.OrdinalIgnoreCase)) return;
        var target = succeeded ? nextName ?? Path.GetFileName(renamedPath!) : Path.GetFileName(path);
        for (var attempt = 0; attempt < 60 && IsLoaded && sequence == _renameSequence; attempt++)
        {
            if (!string.Equals(folder, ResolveFolder?.Invoke(), StringComparison.OrdinalIgnoreCase)) return;
            if (TrySelectByName(target))
            {
                if (succeeded && nextName is not null) BeginInlineRename();
                else Focus(FocusState.Programmatic);
                return;
            }
            await Task.Delay(50);
        }
    }

    private void CancelInlineRename()
    {
        ++_renameSequence;
        if (_renameEditor is { } editor)
        {
            editor.PreviewKeyDown -= RenameEditor_KeyDown;
            editor.LostFocus -= RenameEditor_LostFocus;
            _renameEditor = null;
            if (editor.Parent is Panel parent) parent.Children.Remove(editor);
        }
        if (_renameLabel is { } label) label.Visibility = Visibility.Visible;
        _renameLabel = null;
        _renameRow = null;
        _renamePath = null;
    }
}
