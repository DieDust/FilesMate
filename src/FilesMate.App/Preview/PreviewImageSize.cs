namespace FilesMate.App.Preview;

public static class PreviewImageSize
{
    public static (int Width, int Height) Fit(uint width, uint height, double viewportWidth, double viewportHeight, double dpi)
    {
        if (width == 0 || height == 0) return (1, 1);
        if (!double.IsFinite(dpi) || dpi <= 0) dpi = 1;
        var targetWidth = double.IsFinite(viewportWidth) && viewportWidth > 0 ? viewportWidth * dpi : 1024;
        var targetHeight = double.IsFinite(viewportHeight) && viewportHeight > 0 ? viewportHeight * dpi : 1024;
        var scale = Math.Min(1, Math.Min(Math.Clamp(targetWidth, 1, 2048) / width, Math.Clamp(targetHeight, 1, 2048) / height));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
