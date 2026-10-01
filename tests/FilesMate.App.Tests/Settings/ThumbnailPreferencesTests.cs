using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.App.Tests.Settings;

public sealed class ThumbnailPreferencesTests
{
    [Fact]
    public async Task Legacy_settings_keep_current_thumbnails_and_opt_in_choices_survive_reload()
    {
        var file = Path.Combine(Path.GetTempPath(), "FilesMate-thumbnails-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """{"showHiddenFiles":true,"showFileExtensions":false}""");
            var service = new ExplorerPreferencesService(file);
            var legacy = service.Load();
            Assert.Equal(ThumbnailQuality.Standard, legacy.ThumbnailQuality);
            Assert.False(legacy.ShowFullThumbnails);
            Assert.False(legacy.ShowGridFileSizes);
            foreach (var quality in Enum.GetValues<ThumbnailQuality>())
            {
                var changed = legacy with { ThumbnailQuality = quality, ShowFullThumbnails = true, ShowGridFileSizes = true };
                await service.SaveAsync(changed);
                Assert.Equal(changed, new ExplorerPreferencesService(file).Load());
            }
            await service.SaveAsync(legacy);
            Assert.Equal(legacy, service.Load());
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("999")]
    [InlineData("1")]
    public void Invalid_quality_falls_back_without_losing_other_options(string quality)
    {
        var settings = ExplorerPreferences.Sanitize(null, null, null, null, null, null, null,
            thumbnailQuality: quality, showFullThumbnails: true, showGridFileSizes: true);
        Assert.Equal(ThumbnailQuality.Standard, settings.ThumbnailQuality);
        Assert.True(settings.ShowFullThumbnails);
        Assert.True(settings.ShowGridFileSizes);
    }

    [Theory]
    [InlineData(32, 32, 64, 128)]
    [InlineData(252, 252, 504, 1008)]
    [InlineData(512, 512, 1024, 2048)]
    [InlineData(int.MaxValue, 512, 1024, 2048)]
    public void Quality_preserves_standard_and_bounds_higher_resolution(int pixels, int standard, int high, int ultra)
    {
        Assert.Equal(standard, ThumbnailQualityPolicy.PixelSize(pixels, ThumbnailQuality.Standard));
        Assert.Equal(high, ThumbnailQualityPolicy.PixelSize(pixels, ThumbnailQuality.High));
        Assert.Equal(ultra, ThumbnailQualityPolicy.PixelSize(pixels, ThumbnailQuality.Ultra));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(20)]
    public void Size_line_is_reserved_only_for_large_icons_and_participates_in_selection(double fontSize)
    {
        foreach (var preset in GridSizePreset.All)
        {
            var sized = preset.WithFileSize(true, fontSize);
            Assert.Equal(preset.IconSize >= GridSizePreset.Large.IconSize, sized.ShowsFileSize);
            if (!sized.ShowsFileSize) { Assert.Equal(preset, sized); continue; }
            Assert.True(sized.ItemHeight > preset.ItemHeight);
            Assert.Equal(sized, sized.WithFileSize(true, fontSize));
            Assert.Equal(preset, sized.WithFileSize(false, fontSize));
            Assert.True(sized.TagTop + sized.TagHeight + GridSizePreset.HighlightPad <= sized.ItemHeight);
            var y = preset.ChromeHeight + GridSizePreset.TileSizeGap;
            Assert.Equal(0, GridSizePreset.IndexFromPoint(20, y, 1, 1, sized));
            var hits = new List<int>();
            GridSizePreset.CollectIndicesInRect(20, y, 25, y + 3, 1, 1, sized, hits);
            Assert.Equal([0], hits);
        }
    }
}
