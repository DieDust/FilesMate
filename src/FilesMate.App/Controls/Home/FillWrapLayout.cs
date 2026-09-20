using FilesMate.App.Navigation;

using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace FilesMate.App.Controls.Home;

public sealed class FillWrapLayout : NonVirtualizingLayout
{
    public double MinItemWidth { get; set; } = 188;

    public double MinItemHeight { get; set; } = 92;

    public double MinColumnSpacing { get; set; } = 12;

    public double MinRowSpacing { get; set; } = 12;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        var count = children.Count;
        if (count == 0)
        {
            return new Size(0, 0);
        }

        var columns = FillWrapLayoutMath.Columns(availableSize.Width, count, MinItemWidth, MinColumnSpacing);
        var itemWidth = FillWrapLayoutMath.ItemWidth(availableSize.Width, columns, MinColumnSpacing, MinItemWidth);
        var itemHeight = MinItemHeight;
        foreach (var child in children)
        {
            child.Measure(new Size(itemWidth, availableSize.Height));
            itemHeight = Math.Max(itemHeight, child.DesiredSize.Height);
        }

        var rows = FillWrapLayoutMath.Rows(count, columns);
        var height = (itemHeight * rows) + (MinRowSpacing * Math.Max(0, rows - 1));
        var width = double.IsInfinity(availableSize.Width)
            ? (itemWidth * columns) + (MinColumnSpacing * Math.Max(0, columns - 1))
            : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var count = children.Count;
        if (count == 0)
        {
            return finalSize;
        }

        var columns = FillWrapLayoutMath.Columns(finalSize.Width, count, MinItemWidth, MinColumnSpacing);
        var itemWidth = FillWrapLayoutMath.ItemWidth(finalSize.Width, columns, MinColumnSpacing, MinItemWidth);
        var itemHeight = MinItemHeight;
        foreach (var child in children)
        {
            itemHeight = Math.Max(itemHeight, child.DesiredSize.Height);
        }

        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            children[i].Arrange(new Rect(
                column * (itemWidth + MinColumnSpacing),
                row * (itemHeight + MinRowSpacing),
                itemWidth,
                itemHeight));
        }

        return finalSize;
    }
}
