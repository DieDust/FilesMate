using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.App.Tests.FileSurface;

public sealed class ItemOpeningTests
{
    [Theory]
    [InlineData(ItemOpeningMode.DoubleClick, false, false)]
    [InlineData(ItemOpeningMode.DoubleClick, true, false)]
    [InlineData(ItemOpeningMode.SingleClick, false, true)]
    [InlineData(ItemOpeningMode.SingleClick, true, true)]
    [InlineData(ItemOpeningMode.NameClick, false, false)]
    [InlineData(ItemOpeningMode.NameClick, true, true)]
    public void First_click_distinguishes_the_open_target(ItemOpeningMode mode, bool onName, bool opens)
        => Assert.Equal(opens, Click(new(), "a", mode, onName, 0));

    [Fact]
    public void Double_click_requires_same_item_and_system_time_and_position_limits()
    {
        var tracker = new ItemClickTracker();
        Assert.False(Click(tracker, "a", ItemOpeningMode.DoubleClick, true, 0));
        Assert.False(Click(tracker, "b", ItemOpeningMode.DoubleClick, true, 120));
        Assert.True(Click(tracker, "b", ItemOpeningMode.DoubleClick, true, 240));
        Assert.False(Click(tracker, "b", ItemOpeningMode.DoubleClick, true, 900));
        Assert.False(Click(tracker, "b", ItemOpeningMode.DoubleClick, true, 2000));
        Assert.False(tracker.ShouldOpen("b", ItemOpeningMode.DoubleClick, true, 2100, 30, 20, 500, 3, 3));
        Assert.True(tracker.ShouldOpen("b", ItemOpeningMode.DoubleClick, true, 2250, 30, 20, 500, 3, 3));
    }

    [Theory]
    [InlineData(ItemOpeningMode.SingleClick)]
    [InlineData(ItemOpeningMode.NameClick)]
    public void Second_click_after_navigation_does_not_open_the_new_item_under_the_pointer(ItemOpeningMode mode)
    {
        var tracker = new ItemClickTracker();
        Assert.True(Click(tracker, "parent", mode, true, 0));
        Assert.False(Click(tracker, "child", mode, true, 150));
        Assert.True(Click(tracker, "child", mode, true, 650));
    }

    [Fact]
    public void Selection_or_cancelled_drag_breaks_a_double_click_chain()
    {
        var tracker = new ItemClickTracker();
        Assert.False(Click(tracker, "a", ItemOpeningMode.DoubleClick, true, 0));
        tracker.CancelPendingClick();
        Assert.False(Click(tracker, "a", ItemOpeningMode.DoubleClick, true, 150));
        Assert.True(Click(tracker, "a", ItemOpeningMode.DoubleClick, true, 300));
    }

    [Fact]
    public void Name_mode_keeps_icon_and_metadata_clicks_as_selection_even_when_repeated()
    {
        var tracker = new ItemClickTracker();
        Assert.False(Click(tracker, "a", ItemOpeningMode.NameClick, false, 0));
        Assert.False(Click(tracker, "a", ItemOpeningMode.NameClick, false, 100));
        Assert.True(Click(tracker, "a", ItemOpeningMode.NameClick, true, 200));
    }

    [Fact]
    public async Task File_and_folder_modes_persist_independently_and_legacy_preferences_stay_double_click()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate-Opening-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "explorer.json");
        try
        {
            var service = new ExplorerPreferencesService(path);
            Assert.Equal(ItemOpeningMode.DoubleClick, service.Load().FileOpeningMode);
            File.WriteAllText(path, "{\"showHiddenFiles\":true}");
            Assert.Equal(ItemOpeningMode.DoubleClick, service.Load().FolderOpeningMode);
            foreach (var files in Enum.GetValues<ItemOpeningMode>())
                foreach (var folders in Enum.GetValues<ItemOpeningMode>())
                {
                    await service.SaveAsync(ExplorerPreferences.Default with { FileOpeningMode = files, FolderOpeningMode = folders });
                    Assert.Equal(files, service.Load().OpeningMode(false));
                    Assert.Equal(folders, service.Load().OpeningMode(true));
                }
            File.WriteAllText(path, "{\"fileOpeningMode\":\"unrecognized\",\"folderOpeningMode\":\"2\"}");
            Assert.Equal(ItemOpeningMode.DoubleClick, service.Load().FileOpeningMode);
            Assert.Equal(ItemOpeningMode.DoubleClick, service.Load().FolderOpeningMode);
            var updates = Enum.GetValues<ItemOpeningMode>().Select(mode => service.SaveAsync(ExplorerPreferences.Default with
                { FileOpeningMode = mode, FolderOpeningMode = mode })).ToArray();
            await Task.WhenAll(updates);
            Assert.Equal(ItemOpeningMode.NameClick, service.Load().FileOpeningMode);
            Assert.Equal(ItemOpeningMode.NameClick, service.Load().FolderOpeningMode);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static bool Click(ItemClickTracker tracker, string id, ItemOpeningMode mode, bool onName, long time)
        => tracker.ShouldOpen(id, mode, onName, time, 20, 20, 500, 3, 3);
}
