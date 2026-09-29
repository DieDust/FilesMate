using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class FileTypographyTests
{
    [Fact]
    public async Task Old_profiles_keep_defaults_and_font_preferences_round_trip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-typography-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "appearance.json");
            await File.WriteAllTextAsync(path, "{\"theme\":\"Dark\"}");
            var service = new AppearanceSettingsService(path);
            Assert.Null(service.Load().FileFontFamily);
            Assert.Equal(13, service.Load().FileNameFontSize);
            Assert.Equal(12, service.Load().FileDetailsFontSize);
            Assert.Equal(28, service.Load().FileRowHeight);
            var settings = service.Load() with { FileFontFamily = "Microsoft YaHei UI", FileNameFontSize = 22, FileDetailsFontSize = 16 };
            await service.SaveAsync(settings);
            Assert.Equal(settings, new AppearanceSettingsService(path).Load());
            await File.WriteAllTextAsync(path, "{\"fileFontFamily\":\"file:///font.ttf\",\"fileNameFontSize\":999,\"fileDetailsFontSize\":-1}");
            Assert.Null(service.Load().FileFontFamily);
            Assert.Equal(24, service.Load().FileNameFontSize);
            Assert.Equal(10, service.Load().FileDetailsFontSize);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void Rows_align_to_physical_pixels_at_each_display_scale(double scale)
    {
        var settings = AppearanceSettings.Default with { FileNameFontSize = 22 };
        var height = settings.FileRowHeightForScale(scale);
        Assert.Equal(Math.Round(height * scale), height * scale, 8);
        Assert.True(height >= settings.FileRowHeight && height - settings.FileRowHeight < 1 / scale);
        Assert.Equal(200, FileColumnLayout.IndexFromPoint(50, height * 200 + height / 2, 250, 240, 100, 80, 80, height));
        Assert.Equal(-1, CompactListMetrics.IndexAt(250, height * 8 + height / 2, 400, 200, 240, height));
        Assert.Equal(208, CompactListMetrics.IndexAt(260, height * 8 + height / 2, 400, 200, 240, height));
    }

    [Theory]
    [InlineData(13, 12)]
    [InlineData(24, 20)]
    public void Enlarged_rows_use_the_same_geometry_for_layout_clicks_and_marquee(double name, double details)
    {
        var settings = AppearanceSettings.Default with { FileNameFontSize = name, FileDetailsFontSize = details };
        var height = settings.FileRowHeight;
        var y = height * 3 + height / 2;
        Assert.Equal(3, FileColumnLayout.IndexFromPoint(50, y, 20, 240, 100, 80, 80, height));
        var hits = new List<int>();
        FileColumnLayout.CollectIndicesInRect(30, height * 3 + 3, 100, height * 4 - 3, 20, 240, 100, 80, 80, hits, height);
        Assert.Equal([3], hits);
        var rows = CompactListMetrics.Rows(height * 5 + 8, height);
        Assert.Equal(5, rows);
        Assert.Equal(-1, CompactListMetrics.IndexAt(250, y, 50, rows, 240, height));
        Assert.Equal(8, CompactListMetrics.IndexAt(260, y, 50, rows, 240, height));
        hits.Clear();
        CompactListMetrics.Collect(245, height * 3 + 3, 270, height * 4 - 3, 50, rows, 240, hits, height);
        Assert.Equal([8], hits);
        foreach (var preset in GridSizePreset.All)
        {
            var enlarged = preset.WithFontSize(name);
            Assert.True(enlarged.ItemHeight >= preset.ItemHeight);
            Assert.True(enlarged.TextHeight >= preset.TextHeight);
            Assert.Equal(preset.ItemHeight - preset.TextHeight, enlarged.ItemHeight - enlarged.TextHeight);
            Assert.Equal(preset, preset.WithFontSize(13));
        }
    }
}
