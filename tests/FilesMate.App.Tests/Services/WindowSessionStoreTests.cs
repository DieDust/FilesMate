using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class WindowSessionStoreTests
{
    [Fact]
    public void Save_and_load_round_trip_tabs_and_selected_index()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "window-session.json");
        var store = new WindowSessionStore(file);
        var session = new WindowSession(["C:\\Work", "filesmate:home", "D:\\Photos"], 1);

        store.Save(session);

        var loaded = store.Load();
        Assert.Equal(session.Tabs, loaded.Tabs);
        Assert.Equal(session.SelectedTabIndex, loaded.SelectedTabIndex);
    }

    [Fact]
    public void Missing_or_empty_file_returns_empty_session()
    {
        var missing = new WindowSessionStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "window-session.json"));
        Assert.Empty(missing.Load().Tabs);
    }
}
