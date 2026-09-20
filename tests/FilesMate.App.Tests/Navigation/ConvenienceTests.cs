using FilesMate.App.Controls.FileSurface;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.App.Commands;
using FilesMate.App.Models;

namespace FilesMate.App.Tests.Navigation;

public sealed class ConvenienceTests
{
    [Fact]
    public async Task MemoryPreferenceRoundTripsAndOlderSettingsUseBalancedDefault()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FilesMate-pref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "explorer.json");
            var service = new ExplorerPreferencesService(path);
            File.WriteAllText(path, "{\"showHiddenFiles\":true}");
            Assert.Equal(TabMemoryMode.Balanced, service.Load().TabMemory);
            foreach (var mode in Enum.GetValues<TabMemoryMode>())
            {
                await service.SaveAsync(service.Load() with { TabMemory = mode });
                Assert.Equal(mode, service.Load().TabMemory);
                Assert.True(service.Load().ShowHiddenFiles);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void MemoryPolicyProtectsActiveAndBusyTabsAndHonorsOff()
    {
        var idle = TimeSpan.FromHours(2);
        Assert.False(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Off, idle, false, false, false));
        Assert.False(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Aggressive, idle, true, false, false));
        Assert.False(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Aggressive, idle, false, true, false));
        Assert.False(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Aggressive, idle, false, false, true));
        Assert.False(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Balanced, TimeSpan.FromMinutes(9), false, false, false));
        Assert.True(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Balanced, TimeSpan.FromMinutes(10), false, false, false));
        Assert.True(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Aggressive, TimeSpan.FromMinutes(2), false, false, false));
        Assert.False(TabMemoryPolicy.ShouldHibernate(TabMemoryMode.Moderate, TimeSpan.FromMinutes(29), false, false, false));
    }

    [Fact]
    public void ClosedTabsAreBoundedAndRestoreMostRecentState()
    {
        var history = new ClosedTabHistory();
        for (int i = 0; i < 30; i++) history.Push(new(new($"D:\\{i}", new(true, 2, EntrySort.Name), i * 40)));
        Assert.Equal(16, history.Count);
        for (int i = 29; i >= 14; i--)
        {
            var state = history.Pop()!;
            Assert.Equal($"D:\\{i}", state.Left.Path);
            Assert.Equal(i * 40, state.Left.ScrollOffset);
        }
        Assert.Null(history.Pop());
    }

    [Fact]
    public void SameTypeAndInvertRespectVisibleFilterAndDirectoryKind()
    {
        var store = new EntryStore();
        store.Append([
            new(1, "a.txt", 1, 1, 1, FileAttributes.Normal, EntryKind.File),
            new(2, "B.TXT", 1, 1, 1, FileAttributes.Normal, EntryKind.File),
            new(3, "c.png", 1, 1, 1, FileAttributes.Normal, EntryKind.File),
            new(4, "folder.txt", 0, 1, 1, FileAttributes.Directory, EntryKind.Directory),
            new(5, "hidden.txt", 1, 1, 1, FileAttributes.Hidden, EntryKind.File)]);
        var index = EntryViewIndex.Build(store, EntrySort.Name, new EntryFilter { IncludeHidden = false }, NaturalStringComparer.Instance, 1);
        var selection = new SelectionModel();
        selection.SelectOnly(1);
        selection.SelectSameType(store, index);
        Assert.Equal(new[] { 1, 2 }, selection.Ids.Order().ToArray());
        selection.Invert(store, index);
        Assert.Equal(new[] { 3, 4 }, selection.Ids.Order().ToArray());
        selection.SelectOnly(4);
        selection.SelectSameType(store, index);
        Assert.Equal(new[] { 4 }, selection.Ids.ToArray());
        selection.SelectAll(store, index);
        selection.Invert(store, index);
        Assert.Equal(0, selection.Count);
        Assert.Null(selection.PrimaryId);
    }

    [Fact]
    public void FileConveniencesRequireValidContext()
    {
        var context = CommandContext.SingleFile with { FolderPath = @"D:\\folder", OtherPanePath = @"D:\\other" };
        Assert.True(CommandCatalog.Resolve(AppCommandId.NewFolderWithSelection, context).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.NewFolderWithSelection, context with { IsFolderWritable = false }).Enabled);
        Assert.True(CommandCatalog.Resolve(AppCommandId.CopyToOtherPane, context).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.CopyToOtherPane, context with { OtherPanePath = null }).Enabled);
        Assert.False(CommandCatalog.Resolve(AppCommandId.SelectSameType, context with { SelectionCount = 0 }).Enabled);
    }
}
