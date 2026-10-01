using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    // Drag destinations follow the pixels that are displayed. The layout's
    // ideal metrics can differ from rounded/reflowed cells at fractional DPI.
    private int DropIndexAt(Point scrollerPoint)
    {
        if (scrollerPoint.X < 0 || scrollerPoint.Y < 0
            || scrollerPoint.X >= Scroller.ViewportWidth || scrollerPoint.Y >= Scroller.ViewportHeight) return -1;
        if (_layout == FileLayoutKind.Grid)
        {
            foreach (var tile in _tiles)
            {
                if (!tile.IsLoaded || tile.ViewIndex < 0) continue;
                if (Inside((FrameworkElement)tile.FindName("Root"), scrollerPoint)) return tile.ViewIndex;
                var tags = (FrameworkElement)tile.FindName("TagHost");
                if (tags.ActualWidth > 0 && tags.ActualHeight > 0 && Inside(tags, scrollerPoint)) return tile.ViewIndex;
            }
        }
        else
        {
            foreach (var row in _realized)
            {
                if (row.IsLoaded && row.ViewIndex >= 0 && Inside((FrameworkElement)row.FindName("Root"), scrollerPoint))
                    return row.ViewIndex;
            }
        }
        return -1;
    }

    private bool Inside(FrameworkElement element, Point point)
    {
        var local = element.TransformToVisual(Scroller).Inverse.TransformPoint(point);
        return local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
    }
}
