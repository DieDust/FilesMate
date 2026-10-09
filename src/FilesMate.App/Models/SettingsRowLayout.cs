namespace FilesMate.App.Models;

/// <summary>Shared bounds for settings rows; keep actions usable when labels grow.</summary>
public readonly record struct SettingsRowLayout(bool StackActions, double ActionMaxWidth)
{
    public const double HorizontalInset = 16;
    public const double ColumnGap = 16;
    public const double MinimumTextWidth = 180;

    public static SettingsRowLayout Resolve(double width, double actionWidth, double textScale = 1)
    {
        var contentWidth = Math.Max(0, NonNegative(width) - HorizontalInset * 2);
        var scale = double.IsFinite(textScale) ? Math.Clamp(textScale, 1, 3) : 1;
        var required = MinimumTextWidth * scale + ColumnGap + NonNegative(actionWidth);
        return new SettingsRowLayout(contentWidth < required, contentWidth);
    }

    public static bool NeedsAnotherRow(double width, double firstWidth, double secondWidth, double gap) =>
        NonNegative(width) < NonNegative(firstWidth) + NonNegative(secondWidth) + NonNegative(gap);

    private static double NonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
