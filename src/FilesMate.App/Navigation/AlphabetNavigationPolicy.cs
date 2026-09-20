namespace FilesMate.App.Navigation;

public static class AlphabetNavigationPolicy
{
    // Ten DIPs was too tight for a 10-DIP glyph once fractional display scaling
    // and the button template's line box were applied. Keep enough height for
    // the full glyph and active-letter background; use two columns when needed.
    public const double MinimumSingleColumnCellHeight = 14;
    public const double VerticalPadding = 8;

    public static bool ShouldShow(
        bool enabled,
        int visibleItemCount,
        int minimumItemCount,
        bool isDualPane,
        bool showInDualPane) =>
        enabled
        && visibleItemCount >= Math.Max(0, minimumItemCount)
        && (!isDualPane || showInDualPane);

    public static int ColumnCount(double availableHeight, int labelCount) =>
        availableHeight >= Math.Max(0, labelCount) * MinimumSingleColumnCellHeight + VerticalPadding ? 1 : 2;
}
