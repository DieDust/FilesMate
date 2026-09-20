using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class PreviewImageSizeTests
{
    [Theory]
    [InlineData(12000, 8000, 400, 600, 1.5, 600, 400)]
    [InlineData(8000, 12000, 400, 600, 1.5, 600, 900)]
    [InlineData(160, 100, 400, 600, 2, 160, 100)]
    [InlineData(20000, 20000, 4000, 4000, 2, 2048, 2048)]
    public void Fits_physical_viewport_without_upscaling(uint width, uint height, double viewWidth, double viewHeight, double dpi, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), PreviewImageSize.Fit(width, height, viewWidth, viewHeight, dpi));
    }

    [Fact]
    public void Unmeasured_or_extreme_images_have_bounded_dimensions()
    {
        foreach(var source in new[] { (uint.MaxValue, 1u), (1u, uint.MaxValue), (0u, 0u), (12000u, 8000u) })
        {
            var size = PreviewImageSize.Fit(source.Item1, source.Item2, double.NaN, 0, double.PositiveInfinity);
            Assert.InRange(size.Width, 1, 2048);
            Assert.InRange(size.Height, 1, 2048);
        }
    }
}
