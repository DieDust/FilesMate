namespace FilesMate.App.Models;

public enum ThumbnailQuality
{
    Standard,
    High,
    Ultra,
}

internal static class ThumbnailQualityPolicy
{
    // Standard retains the existing request size and 512-pixel limit. Higher
    // settings decode more source detail without enlarging the tile itself.
    public static int PixelSize(int standardPixels, ThumbnailQuality quality)
    {
        var standard = Math.Clamp(standardPixels, 32, 512);
        return quality switch
        {
            ThumbnailQuality.High => standard * 2,
            ThumbnailQuality.Ultra => standard * 4,
            _ => standard,
        };
    }
}
