namespace FilesMate.App.Navigation;

/// <summary>
/// Column math for home drive cards: fill the row, then wrap.
/// UniformGridLayout keeps leftover columns empty, so three cards stay a left cluster.
/// </summary>
public static class FillWrapLayoutMath
{
    public static int Columns(double availableWidth, int itemCount, double minItemWidth, double columnSpacing)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        var width = UsableWidth(availableWidth, minItemWidth);
        var stride = minItemWidth + columnSpacing;
        var maxColumns = Math.Max(1, (int)((width + columnSpacing) / stride));
        return Math.Min(itemCount, maxColumns);
    }

    public static int Rows(int itemCount, int columns) =>
        itemCount <= 0 || columns <= 0 ? 0 : (itemCount + columns - 1) / columns;

    public static double ItemWidth(double availableWidth, int columns, double columnSpacing, double minItemWidth)
    {
        if (columns <= 0)
        {
            return minItemWidth;
        }

        var width = UsableWidth(availableWidth, minItemWidth);
        return Math.Max(minItemWidth, (width - (columnSpacing * (columns - 1))) / columns);
    }

    private static double UsableWidth(double availableWidth, double minItemWidth) =>
        double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0
            ? minItemWidth
            : availableWidth;
}
