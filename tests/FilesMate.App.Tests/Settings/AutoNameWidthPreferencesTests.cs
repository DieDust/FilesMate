using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.App.Tests.Settings;

public sealed class AutoNameWidthPreferencesTests
{
    [Fact]
    public async Task Legacy_manual_width_is_preserved_when_automatic_sizing_is_enabled_and_disabled()
    {
        var file = Path.Combine(Path.GetTempPath(), "FilesMate-name-width-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """{"detailsNameWidth":312,"showHiddenFiles":true}""");
            var service = new ExplorerPreferencesService(file);
            var original = service.Load();
            Assert.False(original.AutoFitNameColumn);
            await service.SaveAsync(original with { AutoFitNameColumn = true });
            var enabled = new ExplorerPreferencesService(file).Load();
            Assert.True(enabled.AutoFitNameColumn);
            Assert.Equal(312, enabled.DetailsNameWidth);
            Assert.True(enabled.ShowHiddenFiles);
            await service.SaveAsync(enabled with { AutoFitNameColumn = false });
            Assert.Equal(original, new ExplorerPreferencesService(file).Load());
        }
        finally { File.Delete(file); }
    }
}
