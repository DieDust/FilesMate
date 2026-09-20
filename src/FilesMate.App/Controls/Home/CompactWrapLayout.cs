using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace FilesMate.App.Controls.Home;

public sealed class CompactWrapLayout : NonVirtualizingLayout
{
    public double MinItemHeight { get; set; } = 36;

    public double MaxItemWidth { get; set; } = 220;

    public double ColumnSpacing { get; set; } = 8;

    public double RowSpacing { get; set; } = 8;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : MaxItemWidth;
        foreach (var child in context.Children)
        {
            child.Measure(new Size(Math.Min(width, MaxItemWidth), double.PositiveInfinity));
        }

        return MeasureRows(context, width);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var width = Math.Max(0, finalSize.Width);
        var x = 0d;
        var y = 0d;
        var rowHeight = 0d;
        foreach (var child in context.Children)
        {
            var itemWidth = Math.Min(width, Math.Min(MaxItemWidth, Math.Max(1, child.DesiredSize.Width)));
            var itemHeight = Math.Max(MinItemHeight, child.DesiredSize.Height);
            if (x > 0 && x + itemWidth > width)
            {
                x = 0;
                y += rowHeight + RowSpacing;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, itemWidth, itemHeight));
            x += itemWidth + ColumnSpacing;
            rowHeight = Math.Max(rowHeight, itemHeight);
        }

        return finalSize;
    }

    private Size MeasureRows(NonVirtualizingLayoutContext context, double width)
    {
        if (context.Children.Count == 0) return new Size(width, 0);
        var x = 0d;
        var height = 0d;
        var rowHeight = 0d;
        foreach (var child in context.Children)
        {
            var itemWidth = Math.Min(width, Math.Min(MaxItemWidth, Math.Max(1, child.DesiredSize.Width)));
            var itemHeight = Math.Max(MinItemHeight, child.DesiredSize.Height);
            if (x > 0 && x + itemWidth > width)
            {
                height += rowHeight + RowSpacing;
                x = 0;
                rowHeight = 0;
            }

            x += itemWidth + ColumnSpacing;
            rowHeight = Math.Max(rowHeight, itemHeight);
        }

        return new Size(width, height + rowHeight);
    }
}
