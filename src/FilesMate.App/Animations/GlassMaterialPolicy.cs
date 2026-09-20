namespace FilesMate.App.Animations;

/// <summary>Limits background color leakage while preserving a continuous, adjustable glass strength.</summary>
public static class GlassMaterialPolicy
{
    private static double Curve(double opacity) => Math.Pow(Math.Clamp(opacity, 0, 1), 1.35);
    public static double FoundationCoverage(double opacity) => 0.30 + 0.70 * Curve(opacity);
    // Sidebar, command bar and file content share a foundation. Equal transmission
    // prevents wallpaper color from changing the contrast between those surfaces.
    public static double LayerCoverage(double opacity) => 0.40 + 0.60 * Curve(opacity);
    public static double ContentCoverage(double opacity) => LayerCoverage(opacity);
    public static double FloatingCoverage(double opacity) =>
        1 - (1 - FoundationCoverage(opacity)) * (1 - LayerCoverage(opacity));
    public static double CardCoverage(double opacity) => LayerCoverage(opacity);

    // Preserve the palette's channel differences after alpha compositing rather
    // than making high-transparency layers opaque. Always use the original palette.
    public static byte ContrastTint(byte channel, byte neutral, double coverage) =>
        (byte)Math.Clamp(Math.Round(neutral + (channel - neutral) / Math.Clamp(coverage, .01, 1)), 0, 255);
}
