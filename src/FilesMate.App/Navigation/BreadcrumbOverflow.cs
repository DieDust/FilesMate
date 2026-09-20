namespace FilesMate.App.Navigation;

/// <summary>Fits a root and a contiguous suffix; hidden ancestors retain their original order.</summary>
public static class BreadcrumbOverflow
{
    public sealed record Layout(bool ShowRoot, int SuffixStart, double CurrentWidth, bool HasOverflow);

    public static Layout Fit(IReadOnlyList<double> widths, double available, double overflowWidth = 36)
    {
        available = Math.Max(0, available);
        if (widths.Count == 0) return new(false, 0, 0, false);
        var last = widths.Count - 1;
        if (widths.Sum() <= available) return new(false, 0, widths[last], false);
        if (last == 0) return new(false, 0, available, false);

        var budget = Math.Max(0, available - overflowWidth);
        // Do not let a long drive/share name squeeze the current directory out.
        var showRoot = last > 1 && widths[0] + Math.Min(widths[last], 120) <= budget;
        if (showRoot) budget -= widths[0];
        var currentWidth = Math.Min(widths[last], budget);
        budget -= currentWidth;
        var start = last;
        while (start > (showRoot ? 1 : 0) && widths[start - 1] <= budget)
        {
            budget -= widths[--start];
        }
        return new(showRoot, start, currentWidth, true);
    }
}
