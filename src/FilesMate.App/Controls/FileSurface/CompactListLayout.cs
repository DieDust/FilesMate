using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

internal sealed class CompactListLayout : VirtualizingLayout
{
    public CompactListGeometry Geometry { get; private set; } = CompactListGeometry.Uniform(1, 0, CompactListMetrics.ColumnWidth);
    public int Rows => Geometry.Rows;
    public double RowHeight { get; private set; } = FileColumnLayout.RowHeight;
    private double _viewportWidth;
    private double _rasterizationScale = 1;
    public void Configure(CompactListGeometry geometry, double rowHeight, double viewportWidth, double rasterizationScale = 1)
    {
        if (ReferenceEquals(Geometry, geometry) && RowHeight == rowHeight && _viewportWidth == viewportWidth
            && _rasterizationScale == rasterizationScale) return;
        Geometry = geometry; RowHeight = rowHeight; _viewportWidth = viewportWidth;
        _rasterizationScale = rasterizationScale > 0 ? rasterizationScale : 1;
        InvalidateMeasure();
    }
    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var rect = context.RealizationRect;
        var columns = Geometry.ColumnCount;
        var startColumn = Geometry.ClampedColumnAt(rect.X);
        var visibleWidth = double.IsFinite(rect.Width) ? rect.Width : CompactListMetrics.ColumnWidth * 4;
        var endColumn = Math.Min(columns, Geometry.ClampedColumnAt(Math.Max(0, rect.X) + visibleWidth) + 2);
        var first = startColumn * Rows;
        var end = Math.Min(context.ItemCount, endColumn * Rows);
        context.LayoutState = (first, end);
        for (var i = first; i < end; i++)
            context.GetOrCreateElementAt(i).Measure(new Size(Geometry.WidthAt(i / Rows) - CompactListMetrics.ColumnGap, RowHeight));
        var extent = Geometry.ExtentForViewport(_viewportWidth);
        // WinUI rounds the extent and viewport separately. Keep one physical pixel
        // beyond the final column edge so the scroll limit cannot clamp it short.
        if (extent > Geometry.ExtentWidth) extent += 1 / _rasterizationScale;
        return new Size(extent, Math.Min(Rows, context.ItemCount) * RowHeight);
    }
    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (context.LayoutState is ValueTuple<int, int> range)
            for (var i = range.Item1; i < range.Item2; i++)
                context.GetOrCreateElementAt(i).Arrange(new Rect(Geometry.LeftAt(i / Rows), i % Rows * RowHeight,
                    Geometry.WidthAt(i / Rows) - CompactListMetrics.ColumnGap, RowHeight));
        return finalSize;
    }
}
