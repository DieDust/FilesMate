namespace FilesMate.App.Controls.FileSurface;

internal static class InlineRenameViewport
{
    // Keep even partially visible names in place. Only an entirely offscreen
    // target requests movement, and then only to the nearest viewport edge.
    internal static double MinimumOffset(double start, double length, double current, double viewport, double maximum)
    {
        if (!double.IsFinite(start) || !double.IsFinite(length) || !double.IsFinite(current)
            || !double.IsFinite(viewport) || !double.IsFinite(maximum) || length <= 0 || viewport <= 0)
            return current;
        if (start < current + viewport && start + length > current) return current;
        var desired = start < current ? start : start + Math.Min(length, viewport) - viewport;
        return Math.Clamp(desired, 0, Math.Max(0, maximum));
    }
}
