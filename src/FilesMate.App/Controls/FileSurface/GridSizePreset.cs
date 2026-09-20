namespace FilesMate.App.Controls.FileSurface;

public readonly record struct GridSizePreset(
    int Slot,
    double ItemWidth,
    double ItemHeight,
    double IconSize,
    double TextHeight,
    double Gutter)
{
    public const double ContentPadding = 8;

    public const double HighlightPad = 2;

    public const double TileIconTextGap = 2;

    public static readonly GridSizePreset Small = new(96, 72, 80, 32, 32, 2);

    public static readonly GridSizePreset Medium = new(120, 80, 88, 48, 32, 2);

    public static readonly GridSizePreset Large = new(160, 108, 116, 72, 32, 4);

    public static readonly GridSizePreset ExtraLarge = new(200, 140, 152, 96, 36, 4);

    public static readonly GridSizePreset Huge = new(256, 176, 184, 128, 40, 6);

    public static readonly GridSizePreset Jumbo = new(320, 224, 248, 192, 40, 8);

    public static readonly GridSizePreset Maximum = new(384, 280, 312, 256, 40, 8);

    public static readonly GridSizePreset[] All = [Small, Medium, Large, ExtraLarge, Huge, Jumbo, Maximum];

    public static GridSizePreset Default => Medium;

    public double ChromeWidth => Math.Max(IconSize, ItemWidth - (2 * HighlightPad));

    public double ChromeLeft => HighlightPad;

    public static int Columns(double viewportWidth, GridSizePreset preset)
    {
        var available = Math.Max(preset.ItemWidth, viewportWidth - ContentPadding);
        return Math.Max(1, (int)((available + preset.Gutter) / (preset.ItemWidth + preset.Gutter)));
    }

    public static double ItemWidthFor(double viewportWidth, GridSizePreset preset)
    {
        _ = viewportWidth;
        return preset.ItemWidth;
    }

    public static int IndexFromPoint(
        double x,
        double y,
        int count,
        int columns,
        GridSizePreset preset)
    {
        if (count <= 0 || columns <= 0 || x < 0 || y < 0)
        {
            return -1;
        }

        var columnStride = preset.ItemWidth + preset.Gutter;
        var rowStride = preset.ItemHeight + preset.Gutter;
        var column = (int)Math.Floor(x / columnStride);
        var row = (int)Math.Floor(y / rowStride);
        if ((uint)column >= (uint)columns || row < 0)
        {
            return -1;
        }

        var localX = x - (column * columnStride);
        var localY = y - (row * rowStride);
        if (localX >= preset.ItemWidth || localY >= preset.ItemHeight || !HitsContent(localX, localY, preset))
        {
            return -1;
        }

        var index = (row * columns) + column;
        return (uint)index >= (uint)count ? -1 : index;
    }

    public static void CollectIndicesInRect(
        double left,
        double top,
        double right,
        double bottom,
        int count,
        int columns,
        GridSizePreset preset,
        List<int> into)
    {
        if (count <= 0 || columns <= 0 || right <= left || bottom <= top)
        {
            return;
        }

        var columnStride = preset.ItemWidth + preset.Gutter;
        var rowStride = preset.ItemHeight + preset.Gutter;
        var firstColumn = Math.Max(0, (int)Math.Floor(left / columnStride));
        var lastColumn = Math.Min(columns - 1, (int)Math.Floor((right - double.Epsilon) / columnStride));
        var firstRow = Math.Max(0, (int)Math.Floor(top / rowStride));
        var lastRow = (int)Math.Floor((bottom - double.Epsilon) / rowStride);
        var lastIndex = count - 1;
        var lastRowClamped = Math.Min(lastRow, lastIndex / columns);
        if (firstColumn > lastColumn || firstRow > lastRowClamped)
        {
            return;
        }

        for (var row = firstRow; row <= lastRowClamped; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var index = (row * columns) + column;
                if (index > lastIndex)
                {
                    break;
                }

                if (ContentBoundsIntersect(
                        column * columnStride,
                        row * rowStride,
                        preset,
                        left,
                        top,
                        right,
                        bottom))
                {
                    into.Add(index);
                }
            }
        }
    }

    public string ZoomKey => Slot switch
    {
        96 => "Layout_SmallIcons",
        120 => "Layout_MediumIcons",
        160 => "Layout_LargeIcons",
        256 => "Layout_HugeIcons",
        320 => "Layout_JumboIcons",
        384 => "Layout_MaximumIcons",
        _ => "Layout_ExtraLargeIcons",
    };

    public static (FileLayoutKind Layout, GridSizePreset Preset) Step(
        FileLayoutKind layout,
        GridSizePreset preset,
        int direction)
    {
        if (direction == 0)
        {
            return (layout, preset);
        }

        var index = IndexOf(preset);
        if (direction > 0)
        {
            return layout == FileLayoutKind.Grid
                ? (FileLayoutKind.Grid, All[Math.Min(index + 1, All.Length - 1)])
                : (FileLayoutKind.Grid, All[0]);
        }

        if (layout != FileLayoutKind.Grid)
        {
            return (layout, preset);
        }

        return index <= 0
            ? (FileLayoutKind.Details, preset)
            : (FileLayoutKind.Grid, All[index - 1]);
    }

    internal static bool HitsContent(double localX, double localY, GridSizePreset preset)
    {
        var left = preset.ChromeLeft;
        var right = left + preset.ChromeWidth;
        if (localX < left || localX >= right)
        {
            return false;
        }

        var iconTop = HighlightPad;
        var iconBottom = iconTop + preset.IconSize;
        if (localY >= iconTop && localY < iconBottom)
        {
            return true;
        }

        var textTop = iconBottom + TileIconTextGap;
        return localY >= textTop && localY < textTop + preset.TextHeight;
    }

    private static bool ContentBoundsIntersect(
        double cellX,
        double cellY,
        GridSizePreset preset,
        double left,
        double top,
        double right,
        double bottom)
    {
        var contentLeft = cellX + preset.ChromeLeft;
        var contentTop = cellY + HighlightPad;
        var contentRight = contentLeft + preset.ChromeWidth;
        var contentBottom = contentTop + preset.IconSize + TileIconTextGap + preset.TextHeight + HighlightPad;
        return left < contentRight && right > contentLeft && top < contentBottom && bottom > contentTop;
    }

    private static int IndexOf(GridSizePreset preset)
    {
        for (var i = 0; i < All.Length; i++)
        {
            if (All[i] == preset)
            {
                return i;
            }
        }

        return 1;
    }
}
