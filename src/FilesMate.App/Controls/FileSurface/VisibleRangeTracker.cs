namespace FilesMate.App.Controls.FileSurface;

public readonly record struct VisibleRange(int FirstVisible, int LastVisible, int FirstRealized, int LastRealized)
{
    public int RealizedCount => Math.Max(0, LastRealized - FirstRealized + 1);
}

/// <summary>
/// Maps a scroll viewport onto view-index ranges. Overscan is 1–2 viewports, not the whole directory.
/// </summary>
public sealed class VisibleRangeTracker
{
    public VisibleRange Update(double verticalOffset, double viewportHeight, double itemHeight, int count, int overscanViewports = 1)
    {
        if (itemHeight <= 0 || count <= 0 || viewportHeight < 0)
        {
            return default;
        }

        overscanViewports = Math.Clamp(overscanViewports, 0, 2);
        var firstVisible = Math.Clamp((int)(verticalOffset / itemHeight), 0, count - 1);
        var visibleCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / itemHeight) + 1);
        var lastVisible = Math.Clamp(firstVisible + visibleCount - 1, 0, count - 1);
        var overscan = visibleCount * overscanViewports;
        var firstRealized = Math.Max(0, firstVisible - overscan);
        var lastRealized = Math.Min(count - 1, lastVisible + overscan);
        return new VisibleRange(firstVisible, lastVisible, firstRealized, lastRealized);
    }
}
