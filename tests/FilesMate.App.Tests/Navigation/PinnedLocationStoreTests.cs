using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class PinnedLocationStoreTests
{
    [Fact]
    public void Cloud_edit_hide_and_readd_survive_reload_and_do_not_change_pins()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        store.Add(@"C:\Pinned");
        store.SaveCloudOrder(["cloud:OneDrive"]);
        store.EditCloud("cloud:OneDrive", "工作云盘", @"D:\Cloud", @"C:\Cloud");
        NavigationItem[] discovered = [new("cloud:OneDrive", "Cloud", "\uE753", @"C:\Cloud"),
            new("cloud:OneDriveConsumer", "Alias", "\uE753", @"C:\Cloud")];
        var state = new PinnedLocationStore(temp.Path).LoadState();
        var entry = Assert.Single(WindowsNavigationSource.ResolveCloudLocations(discovered, state, _ => true));
        Assert.Equal("工作云盘", entry.Label);
        Assert.Equal(@"D:\Cloud", entry.Target);
        store.HideCloud(@"d:\cloud\");
        Assert.Empty(WindowsNavigationSource.ResolveCloudLocations(discovered, store.LoadState(), _ => true));
        store.AddCloud(@"D:\Cloud");
        state = new PinnedLocationStore(temp.Path).LoadState();
        Assert.Single(WindowsNavigationSource.ResolveCloudLocations(discovered, state, _ => true));
        Assert.Equal([@"C:\Pinned"], state.Paths);
        Assert.Equal(["cloud:onedrive"], state.CloudOrder);
    }

    [Fact]
    public void Cloud_removal_only_changes_references_and_preserves_real_files()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        var folder = Path.GetDirectoryName(temp.Path)!;
        store.AddCloud(folder);
        store.HideCloud(folder);
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(temp.Path));
        Assert.Contains(folder, store.LoadState().HiddenCloudPaths!);
    }

    [Fact]
    public void Cloud_editor_rejects_relative_paths_and_empty_names()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        Assert.Throws<ArgumentException>(() => store.EditCloud("cloud:OneDrive", "Work", "relative"));
        Assert.Throws<ArgumentException>(() => store.EditCloud("cloud:OneDrive", " ", @"C:\Work"));
        Assert.Empty(store.LoadState().CloudOverrides ?? []);
    }

    [Fact]
    public void Add_normalizes_paths_and_deduplicates_case_insensitively()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);

        store.Add(@"C:\Users\Alice\Projects\");
        var paths = store.Add(@"c:\users\alice\projects");

        Assert.Single(paths);
        Assert.Equal(@"C:\Users\Alice\Projects", paths[0]);
        Assert.True(store.IsPinned(@"c:\USERS\alice\PROJECTS\"));
    }

    [Fact]
    public void Remove_is_case_insensitive_and_keeps_other_locations()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        store.Save([@"C:\One", @"D:\Two"]);

        var remaining = store.Remove(@"c:\one\");

        Assert.Equal([@"D:\Two"], remaining);
        Assert.False(store.IsPinned(@"C:\One"));
    }

    [Fact]
    public void Corrupt_json_fails_closed_to_an_empty_list()
    {
        using var temp = new TemporaryFile();
        File.WriteAllText(temp.Path, "not-json");

        var store = new PinnedLocationStore(temp.Path);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Root_paths_keep_the_root_separator()
    {
        Assert.Equal(@"C:\", PinnedLocationStore.Normalize(@"C:\"));
    }

    [Fact]
    public void Hidden_default_ids_are_persisted_without_losing_custom_paths()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        store.Add(@"C:\Work");

        store.HideDefault("home");

        var reopened = new PinnedLocationStore(temp.Path);
        Assert.True(reopened.IsDefaultHidden("home"));
        Assert.Equal([@"C:\Work"], reopened.Load());
    }

    [Fact]
    public void Restore_defaults_keeps_user_added_locations()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        store.Add(@"D:\Projects");
        store.HideDefault("pictures");

        store.RestoreDefaults();

        Assert.False(store.IsDefaultHidden("pictures"));
        Assert.Equal([@"D:\Projects"], store.Load());
    }

    [Fact]
    public void Legacy_array_format_remains_readable()
    {
        using var temp = new TemporaryFile();
        File.WriteAllText(temp.Path, "[\"C:\\\\Legacy\"]");

        var store = new PinnedLocationStore(temp.Path);

        Assert.Equal([@"C:\Legacy"], store.Load());
        Assert.Empty(store.LoadState().HiddenDefaultIds);
        Assert.Empty(store.LoadState().CloudPaths);
    }

    [Fact]
    public void Cloud_folders_are_persisted_separately_from_pins()
    {
        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        store.Add(@"C:\Pinned");
        store.AddCloud(@"C:\Users\Alice\Google Drive\");

        Assert.True(store.IsCloud(@"c:\users\alice\google drive"));
        Assert.False(store.IsPinned(@"C:\Users\Alice\Google Drive"));
        Assert.Equal([@"C:\Pinned"], store.Load());

        var reopened = new PinnedLocationStore(temp.Path);
        Assert.Equal([@"C:\Users\Alice\Google Drive"], reopened.LoadState().CloudPaths);
        Assert.Empty(reopened.RemoveCloud(@"c:\users\alice\google drive"));
        Assert.False(reopened.IsCloud(@"C:\Users\Alice\Google Drive"));
    }

    [Fact]
    public void Item_order_is_applied_and_persisted()
    {
        var ordered = PinnedLocationStore.OrderByIds(
            ["desktop", "documents", "downloads"],
            ["downloads", "desktop"],
            id => id);
        Assert.Equal(["downloads", "desktop", "documents"], ordered);

        using var temp = new TemporaryFile();
        var store = new PinnedLocationStore(temp.Path);
        store.SaveItemOrder(["videos", "desktop"]);
        Assert.Equal(["videos", "desktop"], new PinnedLocationStore(temp.Path).LoadState().ItemOrder);

        store.SaveDriveOrder(["drive:D:", "drive:C:"]);
        store.SaveSectionOrder(["drives", "pinned"]);
        var reopened = new PinnedLocationStore(temp.Path).LoadState();
        Assert.Equal(["drive:d:", "drive:c:"], reopened.DriveOrder);
        Assert.Equal(["drives", "pinned"], reopened.SectionOrder);
    }

    private sealed class TemporaryFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));

        public TemporaryFile()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "pinned.json");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
