namespace FilesMate.App.Controls.FileSurface;

public static class CompactListMetrics
{
    public const double ColumnWidth = 240;
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
