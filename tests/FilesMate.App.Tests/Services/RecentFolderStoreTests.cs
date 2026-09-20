using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class RecentFolderStoreTests
{
    [Fact]
    public void Record_moves_folder_to_front_and_caps_entries()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "recent-folders.json");
        var store = new RecentFolderStore(file);
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        store.Record(Path.Combine(root, "Alpha"));
        store.Record(Path.Combine(root, "Beta"));
        var recent = store.Record(Path.Combine(root, "Alpha"));

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Alpha")), recent[0], ignoreCase: true);
        Assert.Equal(2, recent.Count);
        Assert.DoesNotContain("filesmate:home", recent, StringComparer.OrdinalIgnoreCase);
    }
}
