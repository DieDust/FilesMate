using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Settings;

public sealed class GroupingPreferencesTests
{
    [Fact]
    public void Default_button_cycle_excludes_files_first_but_can_leave_it()
    {
        var prefs = ExplorerPreferences.Default;
        Assert.Equal(EntryGrouping.Mixed, prefs.NextGrouping(EntryGrouping.FoldersFirst));
        Assert.Equal(EntryGrouping.FoldersFirst, prefs.NextGrouping(EntryGrouping.Mixed));
        Assert.Equal(EntryGrouping.FoldersFirst, prefs.NextGrouping(EntryGrouping.FilesFirst));
    }

    [Fact]
    public async Task Custom_cycle_and_default_order_survive_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var prefs = ExplorerPreferences.Default with { DefaultEntryGrouping = EntryGrouping.Mixed,
                GroupingClickCycle = [EntryGrouping.FoldersFirst, EntryGrouping.Mixed, EntryGrouping.FilesFirst] };
            await new ExplorerPreferencesService(path).SaveAsync(prefs);
            var restored = new ExplorerPreferencesService(path).Load();
            Assert.Equal(EntryGrouping.Mixed, restored.DefaultEntryGrouping);
            Assert.Equal(EntryGrouping.FilesFirst, restored.NextGrouping(EntryGrouping.Mixed));
            Assert.Equal(EntryGrouping.FoldersFirst, restored.NextGrouping(EntryGrouping.FilesFirst));
            File.WriteAllText(path, "{\"groupingClickCycle\":[\"Mixed\",\"bogus\",\"Mixed\",\"17\"],\"defaultEntryGrouping\":\"broken\"}");
            restored = new ExplorerPreferencesService(path).Load();
            Assert.Equal([EntryGrouping.Mixed], restored.EffectiveGroupingCycle);
            Assert.Equal(EntryGrouping.FoldersFirst, restored.DefaultEntryGrouping);
            Assert.Equal(EntryGrouping.Mixed, restored.NextGrouping(EntryGrouping.FilesFirst));
            File.WriteAllText(path, "{\"groupingClickCycle\":[]}");
            Assert.Equal(ExplorerPreferences.Default.EffectiveGroupingCycle, new ExplorerPreferencesService(path).Load().EffectiveGroupingCycle);
        }
        finally { File.Delete(path); }
    }
}
