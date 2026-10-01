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
    private ThumbnailQuality _thumbnailQuality;
    private bool _showFullThumbnails;
    private bool _showGridFileSizes;
    private bool _showFolderSizes;
    private bool _showFileExtensions = true;
    private void OpeningPreferencesChanged(object? sender, ExplorerPreferences preferences)
    {
        ApplyThumbnailPreferences(preferences);
        ApplyAutoNamePreference(preferences);
        if (_fileOpeningMode != preferences.FileOpeningMode || _folderOpeningMode != preferences.FolderOpeningMode)
            ApplyOpeningPreferences();
        if (_showAlternatingRows != preferences.ShowAlternatingRows)
        {
            _showAlternatingRows = preferences.ShowAlternatingRows;
            RefreshRealizedSelection();
        }
    }

    private void ApplyThumbnailPreferences(ExplorerPreferences preferences)
    {
        if (_thumbnailQuality != preferences.ThumbnailQuality || _showFullThumbnails != preferences.ShowFullThumbnails
            || _showGridFileSizes != preferences.ShowGridFileSizes || _showFolderSizes != preferences.ShowFolderSizes
            || _showFileExtensions != preferences.ShowFileExtensions)
        {
            _thumbnailQuality = preferences.ThumbnailQuality;
            _showFullThumbnails = preferences.ShowFullThumbnails;
            _showGridFileSizes = preferences.ShowGridFileSizes;
            _showFolderSizes = preferences.ShowFolderSizes;
            _showFileExtensions = preferences.ShowFileExtensions;
            ApplyGridMetrics();
            RefreshTileMetrics();
            RebindVisibleEntries();
            Repeater.InvalidateMeasure();
            ScheduleAutoNameMeasurement();
        }
    }

    private void ApplyOpeningPreferences()
    {
        // A tab may have been unloaded while settings changed. Its retained
        // tiles must pick up thumbnail options when the surface loads again.
        ApplyThumbnailPreferences(App.ExplorerPreferences);
        ApplyAutoNamePreference(App.ExplorerPreferences);
        _fileOpeningMode = App.ExplorerPreferences.FileOpeningMode;
        _folderOpeningMode = App.ExplorerPreferences.FolderOpeningMode;
        _showAlternatingRows = App.ExplorerPreferences.ShowAlternatingRows;
        _pressPointerId = null;
        ItemActivation.CancelPendingClick(this);
        RefreshTileMetrics();
        ApplyDetailsColumns(false);
        RebindVisibleEntries();
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
