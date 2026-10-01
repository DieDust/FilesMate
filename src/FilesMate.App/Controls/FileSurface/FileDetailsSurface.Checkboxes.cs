using FilesMate.App.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private void OnSelectionToggleRequested(object? sender, int entryId)
    {
        var index = sender switch { FileRow row => row.ViewIndex, FileTile tile => tile.ViewIndex, _ => -1 };
        if (IsRenaming || !_items.TryGetEntry(index, out var entry) || entry.Id != entryId) return;
        CancelMarquee();
        ItemActivation.CancelPendingClick(this);
        _selection.Toggle(entryId);
        Focus(FocusState.Programmatic);
        RefreshRealizedSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsSelectionToggleSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is CheckBox { Name: "SelectionBox" }) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
