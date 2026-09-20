using FilesMate.App.Navigation;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Navigation;

public sealed class NavigationSidebarTests
{
    [Fact]
    public void Catalog_keeps_home_pinned_drives_cloud_network_order_and_hides_empty_sections()
    {
        var home = new[] { Item("home", "Home", @"C:\Users\a") };
        var pinned = new[] { Item("docs", "Documents", @"C:\Users\a\Documents") };
        var drives = new[] { Item("c", "Local Disk (C:)", @"C:\") };
        var cloud = new[] { Item("one", "OneDrive", @"C:\Users\a\OneDrive") };
        var sections = NavigationCatalog.Build(pinned, drives, cloud, network: [], home: home);

        Assert.Equal(["home", "pinned", "drives", "cloud"], sections.Select(section => section.Id));
        Assert.Equal(["", "Pinned", "Drives", "Cloud"], sections.Select(section => section.Title));
        Assert.True(sections[1].HasActions);
        Assert.False(sections[0].HasActions);
        Assert.Equal("Pin folder", sections[1].ActionLabel);
        Assert.Equal("Map network drive", sections[2].ActionLabel);
        Assert.Equal("Add cloud storage", sections[3].ActionLabel);
        foreach (var section in sections)
        {
            if (section.ShowTitle)
            {
                Assert.NotEqual(section.Title, section.Title.ToUpperInvariant());
            }

            Assert.NotEmpty(section.Items);
        }
    }

    [Fact]
    public void Item_exposes_the_sidebar_contract_fields()
    {
        var child = Item("child", "Projects", @"C:\Users\a\Projects");
        var item = new NavigationItem(
            "home",
            "Home",
            "\uE80F",
            @"C:\Users\a",
            selected: true,
            expandable: true,
            children: [child],
            badge: "2");

        Assert.Equal("home", item.Id);
        Assert.Equal("Home", item.Label);
        Assert.Equal("\uE80F", item.IconGlyph);
        Assert.Equal(@"C:\Users\a", item.Target);
        Assert.True(item.Selected);
        Assert.True(item.Expandable);
        Assert.Equal(child, Assert.Single(item.Children));
        Assert.Equal("2", item.Badge);
    }

    [Fact]
    public void Select_path_prefers_exact_match_then_longest_prefix()
    {
        var sections = NavigationCatalog.Build(
            [Item("docs", "Documents", @"C:\Users\a\Documents")],
            [Item("c", "Local Disk (C:)", @"C:\")]);

        NavigationCatalog.SelectPath(sections, @"C:\Users\a\Documents");
        Assert.True(Find(sections, "docs").Selected);
        Assert.False(Find(sections, "c").Selected);

        NavigationCatalog.SelectPath(sections, @"C:\Windows");
        Assert.False(Find(sections, "docs").Selected);
        Assert.True(Find(sections, "c").Selected);
    }

    [Fact]
    public void Exact_place_match_ignores_trailing_slashes_and_virtual_uris()
    {
        var sections = NavigationCatalog.Build(
            [],
            [Item("c", "Local Disk (C:)", @"C:\")],
            cloud: [new NavigationItem("cloud:OneDrive", "OneDrive", "\uE753", @"C:\Users\a\OneDrive")],
            tags: [new NavigationItem("tag:1", "测试", "\uE8EC", TagLocation.Uri(1))],
            home: [Item("home", "Home", HomeLocation.Uri)]);

        var cloud = NavigationCatalog.Match(sections, @"C:\Users\a\OneDrive\", exact: true);
        Assert.Equal("cloud:OneDrive", cloud?.Id);
        Assert.Equal("\uE753", cloud?.IconGlyph);

        var tagged = NavigationCatalog.Match(sections, "filesmate:tag:1", exact: true);
        Assert.Equal("测试", tagged?.Label);
        Assert.Equal("\uE8EC", tagged?.IconGlyph);

        Assert.Null(NavigationCatalog.Match(sections, @"C:\Users\a\OneDrive\Documents", exact: true));
        Assert.True(PinnedLocationStore.PathsEqual(@"C:\Users\a\OneDrive\", @"C:\Users\a\OneDrive"));
    }

    [Fact]
    public void Switching_paths_keeps_exactly_one_sidebar_item_selected()
    {
        var sections = NavigationCatalog.Build(
            [
                Item("music", "Music", @"C:\Users\a\Music"),
                Item("videos", "Videos", @"C:\Users\a\Videos"),
            ],
            [Item("c", "Local Disk (C:)", @"C:\")]);

        NavigationCatalog.SelectPath(sections, @"C:\Users\a\Music");
        Assert.Equal("music", Assert.Single(NavigationCatalog.Enumerate(sections), item => item.Selected).Id);

        NavigationCatalog.SelectPath(sections, @"C:\Users\a\Videos");
        Assert.Equal("videos", Assert.Single(NavigationCatalog.Enumerate(sections), item => item.Selected).Id);
    }

    [Fact]
    public void Sidebar_section_starts_expanded_and_reopens_when_its_path_is_selected()
    {
        var sections = NavigationCatalog.Build(
            [Item("docs", "Documents", @"C:\Users\a\Documents")],
            [Item("c", "Local Disk (C:)", @"C:\")]);
        var pinned = Assert.Single(sections, section => section.Id == "pinned");
        Assert.True(pinned.IsExpanded);

        pinned.IsExpanded = false;
        NavigationCatalog.SelectPath(sections, @"C:\Users\a\Documents");
        Assert.True(pinned.IsExpanded);
        Assert.True(Find(sections, "docs").Selected);
    }

    [Fact]
    public void Sidebar_xaml_is_templated_and_settings_is_enabled()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Navigation", "NavigationSidebar.xaml"));
        Assert.Contains("x:Name=\"SectionRepeater\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToUpperInvariant", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Control.Height.StatusBar", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactThicknessConverter", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Hidden\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Sidebar_PointerEntered", xaml, StringComparison.Ordinal);
        Assert.Contains("SectionHeader_Tapped", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsMargin", xaml, StringComparison.Ordinal);
        Assert.Contains("SidebarSectionListStyle", xaml, StringComparison.Ordinal);
        Assert.Contains("SectionHeader_PointerPressed", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BoolToHorizontalAlignmentConverter", xaml, StringComparison.Ordinal);

        var page = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml"));
        Assert.Contains("NavigationSidebar", page, StringComparison.Ordinal);
        Assert.DoesNotContain("PlacesSidebar", page, StringComparison.Ordinal);
        Assert.Contains("Settings", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "SettingsPage.xaml")), StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Controls", "Navigation", "NavigationSidebar.xaml.cs"));
        var menu = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Navigation", "SidebarContextMenu.cs"));
        Assert.Contains("x:Bind Selected", xaml, StringComparison.Ordinal);
        Assert.Contains("BoolToOpacity", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Item_Loaded", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceIcon_Loaded", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Image", xaml, StringComparison.Ordinal);
        Assert.Contains("ContextRequested=\"Item_ContextRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanReorderItems", xaml, StringComparison.Ordinal);
        Assert.Contains("DragItemsCompleted", xaml, StringComparison.Ordinal);
        Assert.Contains("Section_DragItemsCompleted", xaml, StringComparison.Ordinal);
        Assert.Contains("SectionHeader_PointerPressed", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SectionHeader_ManipulationStarted", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ManipulationMode=\"TranslateY\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AllowReorder", File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Navigation", "NavigationCatalog.cs")), StringComparison.Ordinal);
        Assert.Contains("Place_DragItemsCompleted", code, StringComparison.Ordinal);
        Assert.Contains("_sections.Move", code, StringComparison.Ordinal);
        Assert.Contains("SaveItemOrder", code, StringComparison.Ordinal);
        Assert.Contains("SaveDriveOrder", code, StringComparison.Ordinal);
        Assert.Contains("SaveSectionOrder", code, StringComparison.Ordinal);
        Assert.Contains("Section_ContainerContentChanging", code, StringComparison.Ordinal);
        Assert.Contains("SectionAction_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Bind ActionLabel", xaml, StringComparison.Ordinal);
        Assert.Contains("PinnedLocationsChanged", code, StringComparison.Ordinal);
        Assert.Contains("Command_UnpinFromSidebar", menu, StringComparison.Ordinal);
        Assert.Contains("Command_WhoLocks", menu, StringComparison.Ordinal);
        Assert.Contains("OpenInNewTabRequested", code, StringComparison.Ordinal);
        Assert.Contains("OpenInNewWindowRequested", code, StringComparison.Ordinal);
        Assert.Contains("PlaceContextFlyout.Show", code, StringComparison.Ordinal);
        Assert.Contains("WhoLocksRequested", code, StringComparison.Ordinal);
        Assert.Contains("InvokePlaceAction", code, StringComparison.Ordinal);
        Assert.Contains("TagEditor.ShowAsync", code, StringComparison.Ordinal);
        Assert.Contains("TagEditor.ConfirmDeleteAsync", code, StringComparison.Ordinal);
        Assert.Contains("Tag_Edit", menu, StringComparison.Ordinal);
        Assert.Contains("Tag_Delete", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("item.Id == \"home\"", code, StringComparison.Ordinal);
        Assert.Contains("PinFolderAsync", code, StringComparison.Ordinal);
        Assert.Contains("DriveShell.MapNetworkDrive", code, StringComparison.Ordinal);
        Assert.Contains("Sidebar_AddCloudFolder", code, StringComparison.Ordinal);
        Assert.Contains("Sidebar_OneDriveSettings", code, StringComparison.Ordinal);
        Assert.Contains("OpenSettings(\"tags\")", code, StringComparison.Ordinal);
        Assert.Contains("RemoveCloud", code, StringComparison.Ordinal);
        Assert.Contains("TagsChanged", code, StringComparison.Ordinal);
        Assert.Contains("FilesMate.SidebarFlyoutItemStyle", code, StringComparison.Ordinal);
        Assert.Contains("PublishPlaces", code, StringComparison.Ordinal);
        Assert.Contains("replaceWhenTagsEmpty", code, StringComparison.Ordinal);
        Assert.Contains("IsDefaultPinnedId", code, StringComparison.Ordinal);
        Assert.Contains("HideDefault", code, StringComparison.Ordinal);
        Assert.Contains("SectionHeader_Tapped", code, StringComparison.Ordinal);
        Assert.Contains("TryEnqueue", code, StringComparison.Ordinal);
        Assert.Contains("IsExpanded", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindName(\"Selection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellIconBinder.BindPath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PropertyChanged +=", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_hover_uses_a_dedicated_low_latency_opacity_surface()
    {
        var styles = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "NavigationStyles.xaml"));
        var start = styles.IndexOf("x:Key=\"SidebarItemStyle\"", StringComparison.Ordinal);
        var end = styles.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var sidebarStyle = styles[start..end];

        Assert.DoesNotContain("BasedOn=\"{StaticResource QuietButtonStyle}\"", sidebarStyle, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HoverSurface\"", sidebarStyle, StringComparison.Ordinal);
        Assert.Contains("Storyboard.TargetName=\"HoverSurface\"", sidebarStyle, StringComparison.Ordinal);
        Assert.Contains("Duration=\"0:0:0.06\"", sidebarStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate.Motion.Fast", sidebarStyle, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_pins_and_drives_use_distinct_fluent_glyphs()
    {
        using var temp = new PinnedFile();
        var sections = WindowsNavigationSource.Create(new PinnedLocationStore(temp.Path));
        var home = Assert.Single(Assert.Single(sections, section => section.Id == "home").Items);
        Assert.Equal("\uE80F", home.IconGlyph);

        var pinned = Assert.Single(sections, section => section.Id == "pinned").Items;
        var byId = pinned.ToDictionary(item => item.Id, StringComparer.Ordinal);

        Assert.False(byId.ContainsKey("home"));
        Assert.Equal("\uE8B7", byId["desktop"].IconGlyph);
        Assert.Equal("\uE8A5", byId["documents"].IconGlyph);
        Assert.Equal("\uE896", byId["downloads"].IconGlyph);
        Assert.Equal("\uEB9F", byId["pictures"].IconGlyph);
        Assert.Equal("\uE8D6", byId["music"].IconGlyph);
        Assert.Equal("\uE8B2", byId["videos"].IconGlyph);

        var drives = Assert.Single(sections, section => section.Id == "drives").Items;
        Assert.NotEmpty(drives);
        Assert.Contains(drives, item => item.IconGlyph == "\uEDA2");
        Assert.DoesNotContain(drives, item => item.IconGlyph == byId["pictures"].IconGlyph);
        Assert.DoesNotContain(drives, item => item.IconGlyph == byId["downloads"].IconGlyph);
        Assert.Contains(sections, section => section.Id == "cloud");
        Assert.Contains(sections, section => section.Id == "tags");
    }

    [Fact]
    public void Catalog_keeps_empty_cloud_and_drive_sections_so_they_can_be_edited()
    {
        var sections = NavigationCatalog.Build(
            [Item("docs", "Documents", @"C:\Users\a\Documents")],
            drives: [],
            cloud: [],
            tags: []);

        Assert.Empty(Assert.Single(sections, section => section.Id == "drives").Items);
        Assert.Empty(Assert.Single(sections, section => section.Id == "cloud").Items);
        Assert.True(Assert.Single(sections, section => section.Id == "cloud").HasActions);
        Assert.Empty(Assert.Single(sections, section => section.Id == "tags").Items);
        Assert.True(Assert.Single(sections, section => section.Id == "tags").HasActions);
        Assert.Equal("Manage tags", Assert.Single(sections, section => section.Id == "tags").ActionLabel);
    }

    [Fact]
    public void Windows_source_merges_custom_pins_after_known_folders_without_duplicates()
    {
        using var temp = new PinnedFile();
        var store = new PinnedLocationStore(temp.Path);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var custom = Path.Combine(profile, "FilesMate-Test-Pin");
        store.Save([custom, custom + "\\", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)]);

        var sections = WindowsNavigationSource.Create(store);
        var pinned = Assert.Single(sections, section => section.Id == "pinned");

        Assert.Equal(7, pinned.Items.Count);
        var customItem = Assert.Single(pinned.Items, item => item.Id == "custom:" + PinnedLocationStore.Normalize(custom));
        Assert.Equal("FilesMate-Test-Pin", customItem.Label);
        Assert.Equal("\uE8B7", customItem.IconGlyph);
        Assert.Equal(custom, customItem.Target);
    }

    [Fact]
    public void Windows_source_omits_hidden_defaults_but_keeps_drives_and_custom_pins()
    {
        using var temp = new PinnedFile();
        var store = new PinnedLocationStore(temp.Path);
        var custom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FilesMate-Solo");
        store.Add(custom);
        store.HideDefault("home");
        store.HideDefault("pictures");

        var sections = WindowsNavigationSource.Create(store);
        var pinned = Assert.Single(sections, section => section.Id == "pinned");

        Assert.DoesNotContain(pinned.Items, item => item.Id == "home");
        Assert.DoesNotContain(sections, section => section.Id == "home");
        Assert.DoesNotContain(pinned.Items, item => item.Id == "pictures");
        Assert.Contains(pinned.Items, item => item.Id == "custom:" + PinnedLocationStore.Normalize(custom));
        Assert.Contains(sections, section => section.Id == "drives");
    }

    [Fact]
    public void Titled_sections_allow_item_reorder_and_home_stays_first()
    {
        var home = new NavigationSection("home", string.Empty, [Item("home", "Home", @"C:\Users\a")]);
        var pinned = new NavigationSection("pinned", "Pinned", [Item("docs", "Documents", @"C:\Users\a\Documents")]);
        var drives = new NavigationSection(
            "drives",
            "Drives",
            [Item("drive:C:", "Local Disk (C:)", @"C:\"), Item("drive:D:", "Software (D:)", @"D:\")]);
        Assert.False(home.AllowReorder);
        Assert.True(pinned.AllowReorder);
        Assert.True(drives.AllowReorder);

        var ordered = NavigationCatalog.WithSectionOrder(
            [home, pinned, drives, new NavigationSection("tags", "Tags", [])],
            ["tags", "drives", "pinned"]);
        Assert.Equal(["home", "tags", "drives", "pinned"], ordered.Select(section => section.Id));
    }

    [Fact]
    public void Windows_source_applies_saved_drive_order()
    {
        using var temp = new PinnedFile();
        var store = new PinnedLocationStore(temp.Path);
        store.SaveDriveOrder(["drive:D:", "drive:C:"]);
        store.SaveSectionOrder(["tags", "drives", "pinned", "cloud"]);

        var sections = WindowsNavigationSource.Create(store);
        Assert.Equal("home", sections[0].Id);
        var ids = sections.Select(section => section.Id).Where(id => id != "home").ToArray();
        var tags = Array.IndexOf(ids, "tags");
        var drivesIndex = Array.IndexOf(ids, "drives");
        var pinnedIndex = Array.IndexOf(ids, "pinned");
        Assert.True(tags >= 0 && drivesIndex >= 0 && pinnedIndex >= 0);
        Assert.True(tags < drivesIndex && drivesIndex < pinnedIndex);

        var drives = Assert.Single(sections, section => section.Id == "drives").Items;
        var c = drives.ToList().FindIndex(item => item.Id.Equals("drive:C:", StringComparison.OrdinalIgnoreCase));
        var d = drives.ToList().FindIndex(item => item.Id.Equals("drive:D:", StringComparison.OrdinalIgnoreCase));
        if (c >= 0 && d >= 0)
        {
            Assert.True(d < c);
        }
    }

    private static NavigationItem Item(string id, string label, string path) =>
        new(id, label, "\uE80F", path);

    private static NavigationItem Find(IReadOnlyList<NavigationSection> sections, string id) =>
        NavigationCatalog.Enumerate(sections).Single(item => item.Id == id);

    private sealed class PinnedFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));

        public PinnedFile()
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
