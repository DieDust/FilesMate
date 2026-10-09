namespace FilesMate.App.Models;

/// <summary>Accent swatch geometry in DIPs, using the host width after card padding.</summary>
public static class AccentSwatchLayout
{
    public const double SwatchSize = 40;
    public const double Gap = 8;
    public const int MaximumColumns = 4;
    public const double MaximumWidth = MaximumColumns * SwatchSize + (MaximumColumns - 1) * Gap;

    public static int ColumnCount(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0) return 1;
        // Keep a full hit target even before measurement or in a sub-target-width host.
        return (int)Math.Clamp(Math.Floor((availableWidth + Gap) / (SwatchSize + Gap)), 1, MaximumColumns);
    }
}
