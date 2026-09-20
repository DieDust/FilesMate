namespace FilesMate.App.Models;

public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    public const int DefaultWidth = 1440;

    public const int DefaultHeight = 900;

    public const int Unset = int.MinValue;

    public static WindowPlacement Default { get; } = new(Unset, Unset, DefaultWidth, DefaultHeight, false);
}

public static class WindowPlacementMath
{
    public static WindowPlacement Fit(
        WindowPlacement placement,
        int workX,
        int workY,
        int workWidth,
        int workHeight,
        int minWidth,
        int minHeight)
    {
        var maxWidth = Math.Max(minWidth, workWidth);
        var maxHeight = Math.Max(minHeight, workHeight);
        var width = Math.Clamp(placement.Width, minWidth, maxWidth);
        var height = Math.Clamp(placement.Height, minHeight, maxHeight);
        var x = placement.X;
        var y = placement.Y;
        if (x == WindowPlacement.Unset || x + 48 < workX || x >= workX + workWidth)
        {
            x = workX + Math.Max(0, (workWidth - width) / 2);
        }

        if (y == WindowPlacement.Unset || y + 48 < workY || y >= workY + workHeight)
        {
            y = workY + Math.Max(0, (workHeight - height) / 2);
        }

        var maxX = workX + Math.Max(0, workWidth - width);
        var maxY = workY + Math.Max(0, workHeight - height);
        x = Math.Clamp(x, workX, Math.Max(workX, maxX));
        y = Math.Clamp(y, workY, Math.Max(workY, maxY));
        return placement with { X = x, Y = y, Width = width, Height = height };
    }
}
