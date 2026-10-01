using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

internal sealed class CompactListLayout : VirtualizingLayout
{
    public CompactListGeometry Geometry { get; private set; } = CompactListGeometry.Uniform(1, 0, CompactListMetrics.ColumnWidth);
    public int Rows => Geometry.Rows;
    public double RowHeight { get; private set; } = FileColumnLayout.RowHeight;
    private double _viewportWidth;
    public void Configure(CompactListGeometry geometry, double rowHeight, double viewportWidth)
    {
        if (ReferenceEquals(Geometry, geometry) && RowHeight == rowHeight && _viewportWidth == viewportWidth) return;
        Geometry = geometry; RowHeight = rowHeight; _viewportWidth = viewportWidth; InvalidateMeasure();
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
        return new Size(Geometry.ExtentForViewport(_viewportWidth), Math.Min(Rows, context.ItemCount) * RowHeight);
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
