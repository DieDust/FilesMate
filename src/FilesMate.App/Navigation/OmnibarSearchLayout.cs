namespace FilesMate.App.Navigation;

public static class OmnibarSearchLayout
{
    public const double CompactWidth = 36;
    public static bool ShowField(double availableWidth) => availableWidth >= 600;
    public static double Width(double availableWidth, bool active) => active || ShowField(availableWidth)
        ? Math.Clamp(availableWidth * 0.3, 160, 280) : CompactWidth;
}
