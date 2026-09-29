namespace FilesMate.App.Controls.FileSurface;

public readonly record struct GridSizePreset(
    int Slot,
    double ItemWidth,
    double ItemHeight,
    double IconSize,
    double TextHeight,
    double Gutter)
{
    public const double HighlightPad = 2;

    public const double TileIconTextGap = 4;
    public const double TileTagGap = 6;

    public bool ShowsTagNames => IconSize >= 72;
    public double TagHeight => ShowsTagNames ? 20 : 16;
    // Tags occupy a separate row below the uniformly sized icon/name highlight.
    public double TagTop => ChromeHeight + TileTagGap;

    public static readonly GridSizePreset Small = new(96, 72, 100, 32, 36, 2);

    public static readonly GridSizePreset Medium = new(120, 80, 116, 48, 36, 2);

    public static readonly GridSizePreset Large = new(160, 108, 144, 72, 36, 4);

    public static readonly GridSizePreset ExtraLarge = new(200, 140, 168, 96, 36, 4);

    public static readonly GridSizePreset Huge = new(256, 176, 200, 128, 36, 6);

    public static readonly GridSizePreset Jumbo = new(320, 224, 264, 192, 36, 8);

    public static readonly GridSizePreset Maximum = new(384, 280, 328, 256, 36, 8);

    public static readonly GridSizePreset[] All = [Small, Medium, Large, ExtraLarge, Huge, Jumbo, Maximum];

    public static GridSizePreset Default => Medium;

    public GridSizePreset WithFontSize(double fontSize)
    {
        if (fontSize <= 13) return this;
        var textHeight = Math.Max(TextHeight, Math.Ceiling(fontSize * 1.35) * 2);
        return this with { TextHeight = textHeight, ItemHeight = ItemHeight + textHeight - TextHeight };
    }

    public double ChromeWidth => Math.Max(IconSize, ItemWidth - (2 * HighlightPad));

    public double ChromeHeight => HighlightPad + IconSize + TileIconTextGap + TextHeight + HighlightPad;

    public double ChromeLeft => HighlightPad;

    public static int Columns(double viewportWidth, GridSizePreset preset)
    {
        // UniformGridLayout fills the ScrollViewer width without an extra inset.
        // Reserving padding here alone makes hit testing wrap before the actual grid.
        var available = Math.Max(preset.ItemWidth, viewportWidth);
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
        GridSizePreset preset,
        Func<int, bool>? hasTags = null)
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
        var index = (row * columns) + column;
        if ((uint)index >= (uint)count || localX >= preset.ItemWidth || localY >= preset.ItemHeight
            || !HitsContent(localX, localY, preset, localY >= preset.TagTop && hasTags?.Invoke(index) == true))
        {
            return -1;
        }

        return index;
    }

    public static void CollectIndicesInRect(
        double left,
        double top,
        double right,
        double bottom,
        int count,
        int columns,
        GridSizePreset preset,
        List<int> into,
        Func<int, bool>? hasTags = null)
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
                        bottom, hasTags, index))
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

    internal static bool HitsContent(double localX, double localY, GridSizePreset preset, bool hasTags = false)
    {
        var left = preset.ChromeLeft;
        var right = left + preset.ChromeWidth;
        if (localX < left || localX >= right)
        {
            return false;
        }

        return localY >= 0 && localY < preset.ChromeHeight
            || hasTags && localY >= preset.TagTop && localY < preset.TagTop + preset.TagHeight;
    }

    private static bool ContentBoundsIntersect(
        double cellX,
        double cellY,
        GridSizePreset preset,
        double left,
        double top,
        double right,
        double bottom,
        Func<int, bool>? hasTags,
        int index)
    {
        var contentLeft = cellX + preset.ChromeLeft;
        var contentRight = contentLeft + preset.ChromeWidth;
        if (left >= contentRight || right <= contentLeft) return false;
        if (top < cellY + preset.ChromeHeight && bottom > cellY) return true;
        // Empty tag reservations and the gap above visible tags are blank drag space.
        return top < cellY + preset.TagTop + preset.TagHeight && bottom > cellY + preset.TagTop
            && hasTags?.Invoke(index) == true;
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
