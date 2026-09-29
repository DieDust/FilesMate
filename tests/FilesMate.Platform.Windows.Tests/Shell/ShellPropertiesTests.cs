using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class ShellPropertiesTests
{
    [Fact]
    public async Task Font_catalog_lists_installed_horizontal_families_once()
    {
        if (!OperatingSystem.IsWindows()) return;
        var fonts = await InstalledFonts.GetAsync();
        Assert.True(fonts.Length > 10);
        Assert.Contains(fonts, name => name == "Segoe UI");
        Assert.DoesNotContain(fonts, name => name.StartsWith('@'));
        Assert.Equal(fonts.Length, fonts.Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
    }
    [Fact]
    public async Task Enumerates_the_registered_Windows_columns_and_reads_real_image_properties()
    {
        if (!OperatingSystem.IsWindows()) return;
        var columns = await ShellProperties.GetColumnsAsync();
        Assert.True(columns.Count > 100);
        Assert.Contains(columns, c => c.Name == "System.Photo.DateTaken");
        Assert.Contains(columns, c => c.Name == "System.Author");
        Assert.Equal(columns.Count, columns.DistinctBy(c => c.Name).Count());
        var path = Path.Combine(Path.GetTempPath(), "FilesMate-properties-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await File.WriteAllBytesAsync(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1cAAAAASUVORK5CYII="));
            var values = await ShellProperties.ReadAsync(path, ["System.Image.HorizontalSize", "System.Image.VerticalSize", "System.FileExtension"]);
            Assert.Equal(1, values["System.Image.HorizontalSize"].Number);
            Assert.Equal(1, values["System.Image.VerticalSize"].Number);
            Assert.Equal(".png", values["System.FileExtension"].Text);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Missing_files_and_absent_properties_are_empty()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Empty(await ShellProperties.ReadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".not-found"), ["System.Author"]));
    }
}
