using FilesMate.App.Localization;
using FilesMate.Core.Entries;
using FilesMate.Core.Operations;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    internal bool ContainsExternalDragPoint(Point rootPoint)
    {
        if (!IsLoaded || Visibility != Visibility.Visible) return false;
        var point = Scroller.TransformToVisual(null).Inverse.TransformPoint(rootPoint);
        return point.X >= 0 && point.Y >= 0 && point.X < Scroller.ActualWidth && point.Y < Scroller.ActualHeight;
    }

    private void ShowDropDestination(Point point, string destination)
    {
        ArchiveDropCaption.Text = StringTable.Get("Drag_Destination").Replace("{0}",
            Path.GetFileName(Path.TrimEndingDirectorySeparator(destination)) is { Length: > 0 } name ? name : destination,
            StringComparison.Ordinal);
        ArchiveDropHint.Margin = new Thickness(Math.Clamp(point.X + 8, 0, Math.Max(0, Scroller.ActualWidth - 180)), Math.Max(0, point.Y - 36), 0, 0);
        ArchiveDropHint.Visibility = Visibility.Visible;
    }

    internal void UpdateExternalDragFeedback(Point rootPoint, IReadOnlyList<string> paths, DataPackageOperation allowed, bool control, bool shift)
    {
        if (!ContainsExternalDragPoint(rootPoint) || IsPortableDevice || !IsFolderWritable) { ClearDropTarget(); return; }
        var point = Scroller.TransformToVisual(null).Inverse.TransformPoint(rootPoint);
        var index = DropIndexAt(point);
        var folder = _items.TryGetEntry(index, out var entry) && entry.Kind == EntryKind.Directory;
        var previous = _dropTargetViewIndex;
        _dropTargetViewIndex = folder ? index : -1;
        var destination = ResolveDropTargetDirectory() ?? ResolveFolder?.Invoke();
        if (FileDropPolicy.ResolveOperation(paths, destination, true, control, shift,
                allowed.HasFlag(DataPackageOperation.Copy), allowed.HasFlag(DataPackageOperation.Move)) == FileDropOperation.None)
        { ClearDropTarget(); return; }
        UpdateFolderHover(folder ? destination : null);
        if (previous != _dropTargetViewIndex) RefreshRealizedSelection();
        ShowDropDestination(point, destination!);
#if FILESMATE_UI_TEST
        MainWindow.TraceArchiveDrop("DragResolved", new { Updated = DateTime.UtcNow, Point = rootPoint, destination,
            Hint = ArchiveDropCaption.Text, HintVisible = ArchiveDropHint.Visibility.ToString(),
            States = _tiles.Cast<FrameworkElement>().Concat(_realized).Select(element => new {
                Name = element is FileTile t ? t.Entry.Name : ((FileRow)element).Entry.Name,
                State = VisualStateManager.GetVisualStateGroups((FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, 0)).FirstOrDefault()?.CurrentState?.Name
            }).ToArray() });
#endif
    }

    internal void ClearExternalDragFeedback() => ClearDropTarget();

    internal void ShowNativeDropFeedback(Point rootPoint, string? destination, bool accepted)
    {
        if (!accepted || destination is null || !ContainsExternalDragPoint(rootPoint)) { ClearDropTarget(); return; }
        var point = Scroller.TransformToVisual(null).Inverse.TransformPoint(rootPoint);
        var index = DropIndexAt(point);
        var previous = _dropTargetViewIndex;
        _dropTargetViewIndex = _items.TryGetEntry(index, out var entry) && entry.Kind == EntryKind.Directory ? index : -1;
        UpdateFolderHover(_dropTargetViewIndex >= 0 ? destination : null);
        if (previous != _dropTargetViewIndex) RefreshRealizedSelection();
        ShowDropDestination(point, destination);
#if FILESMATE_UI_TEST
        MainWindow.TraceArchiveDrop("DragResolved", new { Updated = DateTime.UtcNow, Point = rootPoint, destination,
            Hint = ArchiveDropCaption.Text, HintVisible = ArchiveDropHint.Visibility.ToString(),
            States = _tiles.Cast<FrameworkElement>().Concat(_realized).Select(element => new {
                Name = element is FileTile t ? t.Entry.Name : ((FileRow)element).Entry.Name,
                State = VisualStateManager.GetVisualStateGroups((FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, 0)).FirstOrDefault()?.CurrentState?.Name
            }).ToArray() });
#endif
    }

}
