using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Services;

public sealed class DetailsColumnTests
{
    [Fact]
    public void Defaults_keep_four_columns_and_normalization_preserves_valid_user_order()
    {
        Assert.Equal([DetailsColumnId.Name, DetailsColumnId.Modified, DetailsColumnId.Type, DetailsColumnId.Size],
            DetailsColumn.Normalize(null).Where(c => c.Visible).Select(c => c.Id));
        var normalized = DetailsColumn.Normalize([new(DetailsColumnId.Created, 200), new(DetailsColumnId.Name, double.NaN, false),
            new(DetailsColumnId.Created, 300), new((DetailsColumnId)999, 10)]);
        Assert.Equal(DetailsColumnId.Created, normalized[0].Id);
        Assert.Equal(200, normalized[0].Width);
        Assert.True(normalized.Single(c => c.Id == DetailsColumnId.Name).Visible);
        Assert.All(normalized, c => Assert.True(double.IsFinite(c.Width) && c.Width >= 64));
        Assert.Equal(Enum.GetValues<DetailsColumnId>().Length, normalized.Length);
    }

    [Fact]
    public async Task Column_order_visibility_and_width_survive_restart_in_both_view_scopes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-column-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "views.json");
            var folder = Path.Combine(directory, "folder");
            var other = Path.Combine(directory, "other");
            var columns = DetailsColumn.Normalize([new(DetailsColumnId.Size, 120), new(DetailsColumnId.Name, 320), new(DetailsColumnId.Created, 180)]);
            var store = new FolderCustomizationStore(file);
            await store.SetViewAsync(folder, new(true, 2, EntrySort.Name, columns));
            var reloaded = new FolderCustomizationStore(file);
            Assert.Equal(columns, (await reloaded.GetAsync(folder)).View!.Columns);
            Assert.Null((await reloaded.GetAsync(other)).View);
            await reloaded.SetGlobalViewAsync(true);
            Assert.Equal(columns, (await reloaded.GetAsync(other)).View!.Columns);
            await reloaded.SetGlobalViewAsync(false);
            Assert.Equal(columns, (await reloaded.GetAsync(folder)).View!.Columns);
            Assert.Null((await reloaded.GetAsync(other)).View);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Alphabet_navigation_defaults_off_for_old_profiles_and_persists_opt_in()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-alphabet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "explorer.json");
            var service = new ExplorerPreferencesService(file);
            Assert.False(service.Load().ShowAlphabetNavigation);
            await File.WriteAllTextAsync(file, "{\"showHiddenFiles\":true}");
            Assert.False(service.Load().ShowAlphabetNavigation);
            Assert.True(service.Load().ShowHiddenFiles);
            await service.SaveAsync(service.Load() with { ShowAlphabetNavigation = true });
            Assert.True(service.Load().ShowAlphabetNavigation);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Mixed_name_preference_defaults_off_and_round_trips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-sort-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ExplorerPreferencesService(Path.Combine(directory, "explorer.json"));
            Assert.False(service.Load().MixChineseAndLatin);
            await service.SaveAsync(ExplorerPreferences.Default with { MixChineseAndLatin = true });
            Assert.True(service.Load().MixChineseAndLatin);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
