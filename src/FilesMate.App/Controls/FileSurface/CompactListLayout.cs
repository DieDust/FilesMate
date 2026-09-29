using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FilesMate.App.Controls.FileSurface;

internal sealed class CompactListLayout : VirtualizingLayout
{
    public int Rows { get; private set; } = 1;
    public double ColumnWidth { get; private set; } = CompactListMetrics.ColumnWidth;
    public double RowHeight { get; private set; } = FileColumnLayout.RowHeight;
    public void Configure(int rows, double width, double rowHeight)
    {
        if (Rows == rows && Math.Abs(ColumnWidth - width) < .1 && RowHeight == rowHeight) return;
        Rows = rows; ColumnWidth = width; RowHeight = rowHeight; InvalidateMeasure();
    }
    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        var rect = context.RealizationRect;
        var columns = (int)Math.Ceiling(context.ItemCount / (double)Rows);
        var startColumn = Math.Clamp((int)Math.Floor(Math.Max(0, rect.X) / ColumnWidth), 0, columns);
        var visibleWidth = double.IsFinite(rect.Width) ? rect.Width : ColumnWidth * 4;
        var endColumn = Math.Min(columns, (int)Math.Ceiling((Math.Max(0, rect.X) + visibleWidth) / ColumnWidth) + 1);
        var first = startColumn * Rows;
        var end = Math.Min(context.ItemCount, endColumn * Rows);
        context.LayoutState = (first, end);
        for (var i = first; i < end; i++)
            context.GetOrCreateElementAt(i).Measure(new Size(ColumnWidth - 8, RowHeight));
        return new Size(columns * ColumnWidth, Math.Min(Rows, context.ItemCount) * RowHeight);
    }
    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (context.LayoutState is ValueTuple<int, int> range)
            for (var i = range.Item1; i < range.Item2; i++)
                context.GetOrCreateElementAt(i).Arrange(new Rect(i / Rows * ColumnWidth, i % Rows * RowHeight,
                    ColumnWidth - 8, RowHeight));
        return finalSize;
    }
}
