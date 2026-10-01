namespace FilesMate.App.Controls.FileSurface;

public static class CompactListMetrics
{
    public const double ColumnWidth = 240;
    // The reference Explorer column is about 1,396 pixels at 175% scaling.
    public const double MaximumColumnWidth = 800;
    public const double MinimumColumnWidth = 96;
    public const double ColumnGap = 8;

    public static double WidthForName(double textWidth, double scale = 1)
    {
        var glyphWidth = Math.Max(FileColumnLayout.GlyphWidth, FileColumnLayout.DetailsIconSize * scale + 4);
        var padding = ColumnGap + FileColumnLayout.ContentLeft + FileColumnLayout.ContentRight
            + FileColumnLayout.AccentWidth + glyphWidth + FileColumnLayout.NameCellPad + 8
            + 4; // Allow for glyph overhang and rounding at fractional display scales.
        return Math.Clamp(Math.Ceiling(textWidth + padding), MinimumColumnWidth * scale, MaximumColumnWidth * scale);
    }

    public static int IndexAt(double x, double y, CompactListGeometry geometry, double rowHeight)
    {
        var column = geometry.ColumnAt(x);
        if (column < 0) return -1;
        var first = column * geometry.Rows;
        var row = IndexAt(x - geometry.LeftAt(column), y, Math.Min(geometry.Rows, geometry.Count - first),
            geometry.Rows, geometry.WidthAt(column), rowHeight);
        return row < 0 ? -1 : first + row;
    }

    public static void Collect(double left, double top, double right, double bottom,
        CompactListGeometry geometry, List<int> result, double rowHeight)
    {
        if (right <= left || bottom <= top || geometry.Count == 0 || right <= 0 || left >= geometry.ExtentWidth) return;
        var firstRow = Math.Max(0, (int)Math.Floor(top / rowHeight));
        var lastRow = Math.Min(geometry.Rows - 1, (int)Math.Ceiling(bottom / rowHeight) - 1);
        for (var column = geometry.ClampedColumnAt(left); column <= geometry.ClampedColumnAt(right); column++)
        {
            if (geometry.LeftAt(column) + geometry.WidthAt(column) - ColumnGap - FileColumnLayout.ContentRight <= left
                || geometry.LeftAt(column) + FileColumnLayout.ContentLeft >= right) continue;
            for (var row = firstRow; row <= lastRow; row++)
            {
                var index = column * geometry.Rows + row;
                if (index < geometry.Count && row * rowHeight + rowHeight - FileColumnLayout.RowInset > top
                    && row * rowHeight + FileColumnLayout.RowInset < bottom) result.Add(index);
            }
        }
    }

    public static int ClampZoom(int percent) => Math.Clamp(percent / 20 * 20, 80, 160);
    public static int StepZoom(int percent, int direction) => ClampZoom(percent + Math.Sign(direction) * 20);
    public static int Rows(double height, double rowHeight = FileColumnLayout.RowHeight) => Math.Max(1, (int)(Math.Max(0, height - 8) / rowHeight));
    public static int IndexAt(double x, double y, int count, int rows, double width, double rowHeight = FileColumnLayout.RowHeight)
    {
        if (x < 0 || y < 0 || rows < 1 || width <= 0) return -1;
        var column = (int)(x / width);
        var row = (int)(y / rowHeight);
        var localX = x - column * width;
        var localY = y - row * rowHeight;
        if (row >= rows || localX < FileColumnLayout.ContentLeft || localX >= width - 8 - FileColumnLayout.ContentRight
            || localY < FileColumnLayout.RowInset || localY >= rowHeight - FileColumnLayout.RowInset) return -1;
        var index = (long)column * rows + row;
        return index < count ? (int)index : -1;
    }
    public static void Collect(double left, double top, double right, double bottom, int count, int rows, double width, List<int> result, double rowHeight = FileColumnLayout.RowHeight)
    {
        if (right <= left || bottom <= top || count <= 0) return;
        var firstColumn = Math.Max(0, (int)Math.Floor(left / width));
        var lastColumn = Math.Min((count - 1) / rows, (int)Math.Ceiling(right / width) - 1);
        var firstRow = Math.Max(0, (int)Math.Floor(top / rowHeight));
        var lastRow = Math.Min(rows - 1, (int)Math.Ceiling(bottom / rowHeight) - 1);
        for (var col = firstColumn; col <= lastColumn; col++)
        for (var row = firstRow; row <= lastRow; row++)
        {
            var index = col * rows + row;
            if (index < count && col * width + width - 8 - FileColumnLayout.ContentRight > left
                && col * width + FileColumnLayout.ContentLeft < right
                && row * rowHeight + rowHeight - FileColumnLayout.RowInset > top
                && row * rowHeight + FileColumnLayout.RowInset < bottom)
                result.Add(index);
        }
    }
}
