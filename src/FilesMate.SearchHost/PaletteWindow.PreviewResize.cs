using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Search;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FilesMate.SearchHost;

public partial class PaletteWindow
{
    private SearchPreviewSize? _previewSize;
    private PreviewResizeState? _previewResize;
    private Vector _previewResizeDelta;
    private sealed record PreviewResizeState(string Edge, Rect Bounds, double OffsetX, double OffsetY, DpiScale Dpi);

    private void PreviewResize_Started(object sender, DragStartedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string edge }) return;
        var dpi = VisualTreeHelper.GetDpi(PreviewCard);
        var origin = PreviewCard.PointToScreen(new Point());
        _previewResize = new(edge, new Rect(origin.X / dpi.DpiScaleX, origin.Y / dpi.DpiScaleY,
            PreviewCard.ActualWidth, PreviewCard.ActualHeight), PreviewPopup.HorizontalOffset, PreviewPopup.VerticalOffset, dpi);
        _previewResizeDelta = new();
        e.Handled = true;
    }

    private void PreviewResize_Delta(object sender, DragDeltaEventArgs e)
    {
        if (_previewResize is not { } state) return;
        _previewResizeDelta += new Vector(e.HorizontalChange, e.VerticalChange);
        var work = new Rect(_workArea.Left / state.Dpi.DpiScaleX, _workArea.Top / state.Dpi.DpiScaleY,
            (_workArea.Right - _workArea.Left) / state.Dpi.DpiScaleX, (_workArea.Bottom - _workArea.Top) / state.Dpi.DpiScaleY);
        var bounds = state.Bounds;
        double left = bounds.Left, right = bounds.Right, top = bounds.Top, bottom = bounds.Bottom;
        var minWidth = Math.Min(240, bounds.Width); var minHeight = Math.Min(200, bounds.Height);
        if (state.Edge.Contains('L')) left = Math.Clamp(left + _previewResizeDelta.X, Math.Max(work.Left, right - 1600), right - minWidth);
        if (state.Edge.Contains('R')) right = Math.Clamp(right + _previewResizeDelta.X, left + minWidth, Math.Min(work.Right, left + 1600));
        if (state.Edge.Contains('T')) top = Math.Clamp(top + _previewResizeDelta.Y, Math.Max(work.Top, bottom - 1600), bottom - minHeight);
        if (state.Edge.Contains('B')) bottom = Math.Clamp(bottom + _previewResizeDelta.Y, top + minHeight, Math.Min(work.Bottom, top + 1600));
        PreviewCard.Width = right - left;
        PreviewCard.Height = bottom - top;
        PreviewPopup.HorizontalOffset = state.OffsetX + left - bounds.Left +
            (PreviewPopup.Placement == PlacementMode.Left ? PreviewCard.Width - bounds.Width : 0);
        PreviewPopup.VerticalOffset = state.OffsetY + top - bounds.Top +
            (PreviewPopup.Placement == PlacementMode.Top ? PreviewCard.Height - bounds.Height : 0);
        e.Handled = true;
    }

    private void PreviewResize_Completed(object sender, DragCompletedEventArgs e)
    {
        if (_previewResize is not { } state) return;
        _previewResize = null;
        if (e.Canceled)
        {
            PreviewCard.Width = state.Bounds.Width; PreviewCard.Height = state.Bounds.Height;
            PreviewPopup.HorizontalOffset = state.OffsetX; PreviewPopup.VerticalOffset = state.OffsetY;
        }
        else
        {
            _previewSize = new(PreviewCard.Width, PreviewCard.Height);
            try { _previewSize.Save(_host.Profile); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { PreviewNote.Text = Loc.Get("Window_SaveSizeFailed"); }
        }
        e.Handled = true;
    }
}
