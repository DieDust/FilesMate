using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class HomeDashboardTests
{
    [Fact]
    public void Capacity_formats_fixed_byte_counts()
    {
        Assert.Equal(0, DriveCapacity.PercentUsed(100, 0));
        Assert.Equal(75, DriveCapacity.PercentUsed(25, 100));
        Assert.Equal(100, DriveCapacity.PercentUsed(0, 100));
        Assert.Equal("0 B", DriveCapacity.FormatBytes(0));
        Assert.Equal("1023 B", DriveCapacity.FormatBytes(1023));
        Assert.Contains("KB", DriveCapacity.FormatBytes(2048), StringComparison.Ordinal);
        var label = DriveCapacity.FormatFreeOfTotal(25_000_000_000, 300_000_000_000);
        Assert.Contains(DriveCapacity.FormatBytes(25_000_000_000), label, StringComparison.Ordinal);
        Assert.Contains(DriveCapacity.FormatBytes(300_000_000_000), label, StringComparison.Ordinal);
        Assert.DoesNotContain(DriveCapacity.FormatBytes(275_000_000_000), label, StringComparison.Ordinal);
        Assert.False(DriveCapacity.IsLowSpace(10, 100));
        Assert.True(DriveCapacity.IsLowSpace(9, 100));
        Assert.True(DriveCapacity.IsLowSpace(25_000_000_000, 300_000_000_000));
        Assert.False(DriveCapacity.IsLowSpace(50_000_000_000, 300_000_000_000));
        Assert.True(DriveCapacity.IsVolumeRoot(@"C:\"));
        Assert.True(DriveCapacity.IsVolumeRoot(@"C:"));
        Assert.False(DriveCapacity.IsVolumeRoot(@"C:\Windows"));
        Assert.False(DriveCapacity.IsVolumeRoot(""));
    }

    [Fact]
    public void Home_is_a_virtual_location_not_the_user_profile()
    {
        Assert.Equal("filesmate:home", HomeLocation.Uri);
        Assert.True(HomeLocation.IsHome("filesmate:home"));
        Assert.True(HomeLocation.TryParse("Home", out var parsed));
        Assert.Equal(HomeLocation.Uri, parsed);
        Assert.False(HomeLocation.IsHome(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }

    [Fact]
    public void Sidebar_home_target_is_the_virtual_location()
    {
        using var temp = new NavigationSidebarTestsPinned();
        var sections = WindowsNavigationSource.Create(new PinnedLocationStore(temp.Path));
        var home = NavigationCatalog.Enumerate(sections).Single(item => item.Id == "home");
        Assert.Equal(HomeLocation.Uri, home.Target);
        Assert.NotEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), home.Target);
    }

    [Fact]
    public async Task Navigating_home_does_not_enumerate_the_filesystem()
    {
        var enumerator = new FakeDirectoryEnumerator();
        await using var vm = new PaneViewModel(
            new ImmediateUiDispatcher(),
            new WindowsPathService(),
            enumerator,
            NaturalStringComparer.Instance);
        vm.Navigate(HomeLocation.Uri);
        await vm.WhenCurrentSessionCompletes;
        Assert.Equal(HomeLocation.Uri, vm.AddressText);
        Assert.Equal(0, enumerator.Started);
        Assert.False(vm.IsLoading);
        Assert.True(vm.ItemCount > 0);
        Assert.False(vm.CanGoUp);
        Assert.Null(vm.Store);
    }

    [Fact]
    public void Home_surface_is_an_overlay_and_does_not_swap_the_repeater()
    {
        var page = File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "Views",
            "NavigatorPage.xaml"));
        Assert.Contains("x:Name=\"HomeContainer\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileSurface\"", page, StringComparison.Ordinal);
        var surface = File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "Views",
            "NavigatorPage.xaml.cs"));
        Assert.Contains("HomeLocation.Uri", surface, StringComparison.Ordinal);
        var dashboard = File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "Controls",
            "Home",
            "HomeDashboard.xaml"));
        Assert.DoesNotContain("x:Name=\"Banner\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesMate-256", dashboard, StringComparison.Ordinal);
        Assert.Contains("ApplyHomeSurface", surface, StringComparison.Ordinal);
        Assert.Contains("ShowHomeSurface", surface, StringComparison.Ordinal);
        Assert.Contains("home.Opacity", surface, StringComparison.Ordinal);
        Assert.Contains("TagLocation.IsTag", surface, StringComparison.Ordinal);
        Assert.Contains("InvokePlaceAction", surface, StringComparison.Ordinal);
        Assert.Contains("PlaceActionRequested", surface, StringComparison.Ordinal);
        Assert.Contains("WhoLocksRequested", page, StringComparison.Ordinal);
        Assert.DoesNotContain("HomeDashboard.Visibility", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("HomePlaces.Drives()", File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "Navigation",
            "PaneViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("FileSurface.Visibility", surface, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLayout(FileLayoutKind.Grid)", surface, StringComparison.Ordinal);
        var dashboardCode = File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "Controls",
            "Home",
            "HomeDashboard.xaml.cs"));
        Assert.Contains("Reload();", dashboardCode, StringComparison.Ordinal);
        Assert.Contains("HomePlaceButtonStyle", dashboardCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Width = 248", dashboardCode, StringComparison.Ordinal);
        Assert.DoesNotContain("fill.Width", dashboardCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged", dashboardCode, StringComparison.Ordinal);
        Assert.DoesNotContain("UniformGridLayout", dashboard, StringComparison.Ordinal);
        Assert.Contains("FillWrapLayout", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DriveHost\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CloudHost\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TagHost\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HomePlaceCard\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HomeTagChip\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("CompactWrapLayout", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CustomizeButton\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SectionsHost\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("HomePlaces.Cloud(_places)", dashboardCode, StringComparison.Ordinal);
        Assert.Contains("HomePlaces.Tags(", dashboardCode, StringComparison.Ordinal);
        Assert.Contains("CapacityLabel", dashboard, StringComparison.Ordinal);
        Assert.Contains("VolumeName", dashboard, StringComparison.Ordinal);
        Assert.Contains("ContextRequested=\"Place_ContextRequested\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("PlaceContextFlyout.Show", dashboardCode, StringComparison.Ordinal);
        Assert.Contains("PlaceActionRequested", dashboardCode, StringComparison.Ordinal);
        Assert.Contains("IsLowSpace", dashboard, StringComparison.Ordinal);
        Assert.Contains("PercentToStar", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("ScaleTransform", dashboard, StringComparison.Ordinal);
        Assert.Contains("SystemFillColorCriticalBrush", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("PercentLabel", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("UsedOfTotal", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", dashboard, StringComparison.Ordinal);
        var dashboardMark = page.IndexOf("x:Name=\"HomeContainer\"", StringComparison.Ordinal);
        Assert.True(dashboardMark >= 0);
        var dashboardTag = page[dashboardMark..page.IndexOf("/>", dashboardMark, StringComparison.Ordinal)];
        Assert.Contains("Visibility=\"Collapsed\"", dashboardTag, StringComparison.Ordinal);
    }

    [Fact]
    public void Drive_cards_fill_the_available_row_then_wrap()
    {
        Assert.Equal(0, FillWrapLayoutMath.Columns(1200, 0, 188, 12));
        Assert.Equal(3, FillWrapLayoutMath.Columns(1200, 3, 188, 12));
        Assert.Equal(392, FillWrapLayoutMath.ItemWidth(1200, 3, 12, 188), 0);
        Assert.Equal(2, FillWrapLayoutMath.Columns(400, 3, 188, 12));
        Assert.Equal(2, FillWrapLayoutMath.Rows(3, 2));
        Assert.Equal(1, FillWrapLayoutMath.Columns(188, 3, 188, 12));
    }

    [Fact]
    public void Tag_places_use_the_virtual_tag_location()
    {
        var tags = HomePlaces.Tags(
        [
            new TagDefinition(3, "Work", "#2F80ED", 0),
        ]);
        var tag = Assert.Single(tags);
        Assert.Equal("Work", tag.Label);
        Assert.Equal(LocationCaption.TagGlyph, tag.Glyph);
        Assert.Equal(TagLocation.Uri(3), tag.Path);
    }

    [Fact]
    public async Task Home_layout_persists_order_and_hidden_sections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "home-layout.json");
        try
        {
            var service = new HomeLayoutSettingsService(path);
            var changed = HomeLayoutSettings.Default
                .Move(HomeSectionKind.Tags, -1)
                .SetVisible(HomeSectionKind.Cloud, false);
            await service.SaveAsync(changed);
            var loaded = service.Load();
            Assert.Equal(changed.Order, loaded.Order);
            Assert.Contains(HomeSectionKind.Cloud, loaded.Hidden);
            Assert.DoesNotContain(HomeSectionKind.Tags, loaded.Hidden);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Home_layout_sanitizes_unknown_and_missing_sections()
    {
        var settings = HomeLayoutSettings.Sanitize(
            ["Tags", "Unknown", "Tags", "Drives"],
            ["Cloud", "Unknown"]);
        Assert.Equal(HomeSectionKind.Tags, settings.Order[0]);
        Assert.Equal(HomeSectionKind.Drives, settings.Order[1]);
        Assert.Equal(Enum.GetValues<HomeSectionKind>().Length, settings.Order.Count);
        Assert.Equal([HomeSectionKind.Cloud], settings.Hidden);
    }

    [Fact]
    public void Home_places_share_the_sidebar_context_menu()
    {
        var folder = HomePlaces.Place(new HomeFolderItem("documents", "Documents", "\uE8A5", @"C:\Users\a\Documents"));
        var drive = HomePlaces.Place(new HomeDriveItem("System", "C:", @"C:\", "\uEDA2", 25_000_000_000, 300_000_000_000));
        Assert.Equal("documents", folder.Id);
        Assert.Equal("drive:C:", drive.Id);
        Assert.Equal("System (C:)", drive.Label);
        Assert.Contains(SidebarContextAction.WhoLocks, SidebarContextMenu.For(folder));
        Assert.Contains(SidebarContextAction.WhoLocks, SidebarContextMenu.For(drive));
        Assert.Contains(SidebarContextAction.CopyPath, SidebarContextMenu.For(drive));
        Assert.Contains(SidebarContextAction.Properties, SidebarContextMenu.For(drive));
        Assert.Equal(
            SidebarContextMenu.For(folder),
            SidebarContextMenu.For(new NavigationItem("documents", "Documents", "\uE8A5", @"C:\Users\a\Documents")));
    }

    private sealed class NavigationSidebarTestsPinned : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));

        public NavigationSidebarTestsPinned()
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
