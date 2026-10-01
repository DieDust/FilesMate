#if FILESMATE_UI_TEST
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    internal void CancelRenameForMutationSmoke() => CancelInlineRename();
    internal async Task<object> RunDetailsBlankSmokeAsync()
    {
        SetLayout(FileLayoutKind.Details);
        Scroller.ChangeView(0, 0, null, true);
        await Task.Delay(150);
        UpdateLayout();
        var row = (FileRow)Repeater.TryGetElement(0);
        var root = (FrameworkElement)row.FindName("Root");
        var bounds = root.TransformToVisual(Scroller).TransformBounds(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        var blank = new Point(Math.Min(Scroller.ViewportWidth - 12, bounds.Right + 80), bounds.Top + bounds.Height / 2);
        Require(blank.X > bounds.Right + 20, "Fixture does not have empty space after the last column");
        Require(row.MinWidth == 0 && ViewIndexFromPoint(blank.X, blank.Y) == -1, "Right-side blank space belongs to a row");
        var hits = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(
            Scroller.TransformToVisual(XamlRoot.Content).TransformPoint(blank), XamlRoot.Content);
        Require(!hits.OfType<FileRow>().Any(), "A file row still receives hits in the right blank area");
        PreparePointerSelection(blank, 1);
        Require(_pressViewIndex == -1 && !_dragCandidate, "Blank-space press starts a file drag");
        AdvanceSelectionPointer(new Point(bounds.Left + 60, RowHeight * 2 - 5));
        Require(_dragging && _selection.Count == 2 && !_externalDragStarted, "Marquee cannot select from the right blank area");
        CancelMarquee();
        PreparePointerSelection(blank, 2);
        AdvanceSelectionPointer(new Point(blank.X + 12, RowHeight * 2 - 5));
        Require(_selection.Count == 0, "Marquee wholly in the blank area selects files");
        CancelMarquee();
        // Right-click routing uses the same point-to-entry lookup.
        Require(ViewIndexFromPoint(blank.X, blank.Y) == -1, "Right-click would target a file instead of the folder background");
        return new { RowRight = bounds.Right, ViewportWidth = Scroller.ViewportWidth, BlankWidth = Scroller.ViewportWidth - bounds.Right,
            BlankHitContainsRow = false, MarqueeFromBlankSelected = 2, BlankOnlySelected = 0, RightClickIsBackground = true };
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
#endif
