namespace FilesMate.App.Controls.FileSurface;

/// <summary>Column-major positions with one measured width per column, rather than per entry.</summary>
public sealed class CompactListGeometry
{
    private readonly double[] _widths;
    private readonly double[] _lefts;

    public CompactListGeometry(int rows, int count, IReadOnlyList<double> widths)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(widths);
        Rows = rows;
        Count = count;
        if (widths.Count != (int)Math.Ceiling(count / (double)rows)) throw new ArgumentException("One width per column is required.", nameof(widths));
        _widths = new double[widths.Count];
        _lefts = new double[widths.Count];
        for (var i = 0; i < widths.Count; i++)
        {
            if (!double.IsFinite(widths[i]) || widths[i] <= 0) throw new ArgumentOutOfRangeException(nameof(widths));
            _lefts[i] = ExtentWidth;
            _widths[i] = widths[i];
            ExtentWidth += widths[i];
        }
    }

    public int Rows { get; }
    public int Count { get; }
    public int ColumnCount => _widths.Length;
    public double ExtentWidth { get; }

    private CompactListGeometry(int rows, int count, double[] widths, double[] lefts, double extentWidth)
    {
        Rows = rows;
        Count = count;
        _widths = widths;
        _lefts = lefts;
        ExtentWidth = extentWidth;
    }

    // Keep old layout snapshots immutable. A visible-column publication needs
    // no intermediate full width array, and only the affected prefix suffix
    // must be recomputed. Incomplete columns retain their published widths.
    internal CompactListGeometry WithMeasuredWidths(ReadOnlySpan<double> widths,
        ReadOnlySpan<int> measuredEntries, int first, int last)
    {
        if (widths.Length != ColumnCount || measuredEntries.Length != ColumnCount)
            throw new ArgumentException("One measurement per column is required.");
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(last, ColumnCount - 1);
        if (last < first) return this;
        double[]? updated = null;
        var changedFirst = -1;
        for (var c = first; c <= last; c++)
        {
            if (measuredEntries[c] != Math.Min(Rows, Count - c * Rows) || _widths[c] == widths[c]) continue;
            if (!double.IsFinite(widths[c]) || widths[c] <= 0) throw new ArgumentOutOfRangeException(nameof(widths));
            if (updated is null)
            {
                updated = (double[])_widths.Clone();
                changedFirst = c;
            }
            updated[c] = widths[c];
        }
        if (updated is null) return this;
        var lefts = new double[ColumnCount];
        Array.Copy(_lefts, lefts, changedFirst);
        var extent = _lefts[changedFirst];
        for (var c = changedFirst; c < ColumnCount; c++)
        {
            lefts[c] = extent;
            extent += updated[c];
        }
        return new(Rows, Count, updated, lefts, extent);
    }

    public double WidthAt(int column) => ColumnCount == 0 ? CompactListMetrics.MinimumColumnWidth : _widths[Math.Clamp(column, 0, ColumnCount - 1)];
    public double LeftAt(int column) => ColumnCount == 0 ? 0 : column >= ColumnCount ? ExtentWidth : _lefts[Math.Max(0, column)];

    public int ColumnAt(double x)
    {
        if (!double.IsFinite(x) || x < 0 || x >= ExtentWidth) return -1;
        var found = Array.BinarySearch(_lefts, x);
        return found >= 0 ? found : ~found - 1;
    }

    public int ClampedColumnAt(double x) => ColumnCount == 0 ? 0 : x >= ExtentWidth ? ColumnCount - 1 : Math.Max(0, ColumnAt(Math.Max(0, x)));

    public double WheelOffset(double current, long steps, double maximum)
    {
        current = Math.Clamp(current, 0, Math.Max(0, maximum));
        if (ColumnCount == 0 || steps == 0) return current;
        var column = ClampedColumnAt(current);
        if (column + 1 < ColumnCount && LeftAt(column + 1) - current < .5) column++;
        var forward = steps > 0;
        // From a scrollbar position inside a column, the first backward notch
        // brings that column's left edge into view instead of skipping it.
        if (!forward && current > LeftAt(column) + .5) steps++;
        var nextColumn = (int)Math.Clamp(column + steps, 0, ColumnCount - 1L);
        var target = Math.Clamp(LeftAt(nextColumn), 0, Math.Max(0, maximum));
        return forward ? Math.Max(current, target) : Math.Min(current, target);
    }

    public double ExtentForViewport(double viewportWidth)
    {
        if (ColumnCount == 0 || viewportWidth <= 0) return ExtentWidth;
        var maximum = Math.Max(0, ExtentWidth - viewportWidth);
        var column = ClampedColumnAt(maximum);
        if (maximum - LeftAt(column) <= .5 || column + 1 >= ColumnCount) return ExtentWidth;
        // Allow the last visible page to start at a column boundary. Otherwise
        // the scroll limit clamps an aligned wheel target to a partial column.
        return Math.Max(ExtentWidth, LeftAt(column + 1) + viewportWidth);
    }

    public double RevealOffset(int index, double current, double viewportWidth)
    {
        var column = index / Rows;
        var left = LeftAt(column);
        var width = WidthAt(column);
        var target = left < current || width > viewportWidth ? left
            : left + width > current + viewportWidth ? left + width - viewportWidth : current;
        // Revealing a selection must not leave the first visible column clipped either.
        // Choose the first column boundary that fully exposes the requested entry.
        var first = ClampedColumnAt(target);
        if (target - LeftAt(first) > .5) first++;
        target = LeftAt(Math.Min(first, column));
        return Math.Clamp(target, 0, Math.Max(0, ExtentForViewport(viewportWidth) - viewportWidth));
    }

    public VisibleRange VisibleRange(double offset, double viewportWidth)
    {
        if (ColumnCount == 0) return new(0, -1, 0, -1);
        return new(ClampedColumnAt(offset), ClampedColumnAt(offset + viewportWidth),
            ClampedColumnAt(offset - viewportWidth), ClampedColumnAt(offset + viewportWidth * 2));
    }

    public static CompactListGeometry Uniform(int rows, int count, double width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var widths = new double[(int)Math.Ceiling(count / (double)rows)];
        if (widths.Length > 0 && (!double.IsFinite(width) || width <= 0)) throw new ArgumentOutOfRangeException(nameof(width));
        Array.Fill(widths, width);
        var lefts = new double[widths.Length];
        var extent = 0d;
        for (var c = 0; c < widths.Length; c++)
        {
            lefts[c] = extent;
            extent += width;
        }
        return new(rows, count, widths, lefts, extent);
    }
}
