using FilesMate.App.Input;
using FilesMate.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private ItemOpeningMode _fileOpeningMode;
    private ItemOpeningMode _folderOpeningMode;
    private bool _showAlternatingRows = true;
    private void OpeningPreferencesChanged(object? sender, ExplorerPreferences preferences)
    {
        if (_fileOpeningMode != preferences.FileOpeningMode || _folderOpeningMode != preferences.FolderOpeningMode)
            ApplyOpeningPreferences();
        if (_showAlternatingRows != preferences.ShowAlternatingRows)
        {
            _showAlternatingRows = preferences.ShowAlternatingRows;
            RefreshRealizedSelection();
        }
    }

    private void ApplyOpeningPreferences()
    {
        _fileOpeningMode = App.ExplorerPreferences.FileOpeningMode;
        _folderOpeningMode = App.ExplorerPreferences.FolderOpeningMode;
        _showAlternatingRows = App.ExplorerPreferences.ShowAlternatingRows;
        _pressPointerId = null;
        ItemActivation.CancelPendingClick(this);
        RefreshTileMetrics();
        ApplyDetailsColumns(false);
        RefreshRealizedSelection();
    }

    private bool IsNameAt(int index, Point position)
    {
        if (index < 0 || Repeater.TryGetElement(index) is not FrameworkElement element
            || element.FindName("NameText") is not TextBlock name || name.Visibility != Visibility.Visible) return false;
        var bounds = name.TransformToVisual(Scroller).TransformBounds(new Rect(0, 0, name.ActualWidth, name.ActualHeight));
        return bounds.Contains(position);
    }

    private bool GridEntryHasTags(int index) => _items.TryGetEntry(index, out var entry)
        && ResolveTags?.Invoke(entry) is { Count: > 0 };
}
