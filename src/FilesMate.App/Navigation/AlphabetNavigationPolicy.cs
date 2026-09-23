namespace FilesMate.App.Navigation;

public static class AlphabetNavigationPolicy
{
    // Leave room around each letter's marker in a single column. Shorter
    // surfaces use two columns so the buttons remain readable and separated.
    public const double MinimumSingleColumnCellHeight = 22;
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
