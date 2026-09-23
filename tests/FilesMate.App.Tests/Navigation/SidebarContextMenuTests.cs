using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class SidebarContextMenuTests
{
    [Fact]
    public void Portable_devices_do_not_offer_filesystem_mutations()
    {
        var device = new FilesMate.Platform.Windows.Shell.PortableDeviceLocation(
            FilesMate.Platform.Windows.Shell.PortableDeviceLocation.ComputerPrefix + @"\\?\usb#test#"
            + FilesMate.Platform.Windows.Shell.PortableDeviceLocation.InterfaceId, "iPad", []);
        Assert.Equal([SidebarContextAction.OpenInNewTab, SidebarContextAction.OpenInNewWindow],
            SidebarContextMenu.For(new NavigationItem("device:test", device.Name, "\uE8EA", device.Uri)));
        Assert.Equal(device.Uri, LaunchPath.Parse(["FilesMate.App.exe", device.Uri]).Folder);
        Assert.Equal(device.Uri, LaunchPath.Parse(["FilesMate.App.exe", "/open", device.Uri]).Folder);
    }
    [Theory]
    [InlineData("cloud:OneDrive")]
    [InlineData("cloud:WpsCloud")]
    [InlineData("cloud:custom:C:\\Cloud")]
    public void All_cloud_entries_allow_edit_and_remove_without_file_delete(string id)
    {
        var actions = SidebarContextMenu.For(new NavigationItem(id, "Cloud", "\uE753", @"C:\Cloud"));
        Assert.Contains(SidebarContextAction.EditCloud, actions);
        Assert.Contains(SidebarContextAction.RemoveCloud, actions);
        Assert.DoesNotContain(SidebarContextAction.Unpin, actions);
        Assert.DoesNotContain(SidebarContextAction.DisconnectNetwork, actions);
    }

    [Fact]
    public void Tag_items_expose_edit_and_delete_instead_of_folder_actions()
    {
        var tag = new NavigationItem("tag:1", "测试2", "\uE8EC", TagLocation.Uri(1));
        Assert.Equal(
            [
                SidebarContextAction.OpenInNewTab,
                SidebarContextAction.OpenInNewWindow,
                SidebarContextAction.EditTag,
                SidebarContextAction.DeleteTag,
            ],
            SidebarContextMenu.For(tag));
        Assert.DoesNotContain(SidebarContextAction.Properties, SidebarContextMenu.For(tag));
        Assert.DoesNotContain(SidebarContextAction.Unpin, SidebarContextMenu.For(tag));
    }

    [Fact]
    public void Home_can_open_elsewhere_but_cannot_be_unpinned()
    {
        var home = new NavigationItem("home", "Home", "\uE80F", HomeLocation.Uri);
        Assert.Equal(
            [
                SidebarContextAction.OpenInNewTab,
                SidebarContextAction.OpenInNewWindow,
            ],
            SidebarContextMenu.For(home));
        Assert.False(SidebarContextMenu.CanUnpin(home));
    }

    [Fact]
    public void Pinned_folder_keeps_unpin_copy_and_properties()
    {
        var documents = new NavigationItem("documents", "Documents", "\uE8A5", @"C:\Users\a\Documents");
        Assert.Equal(
            [
                SidebarContextAction.OpenInNewTab,
                SidebarContextAction.OpenInNewWindow,
                SidebarContextAction.Unpin,
                SidebarContextAction.OpenTerminal,
                SidebarContextAction.CopyPath,
                SidebarContextAction.WhoLocks,
                SidebarContextAction.Properties,
            ],
            SidebarContextMenu.For(documents));
    }

    [Fact]
    public void Removable_drive_adds_eject_before_copy_and_properties()
    {
        var drive = new NavigationItem("drive:E", "USB (E:)", "\uEDA2", @"E:\");
        Assert.Equal(
            [
                SidebarContextAction.OpenInNewTab,
                SidebarContextAction.OpenInNewWindow,
                SidebarContextAction.Eject,
                SidebarContextAction.OpenTerminal,
                SidebarContextAction.CopyPath,
                SidebarContextAction.WhoLocks,
                SidebarContextAction.Properties,
            ],
            SidebarContextMenu.For(drive, removableDrive: true));
    }

    [Fact]
    public void Filesystem_places_can_ask_who_locks()
    {
        var documents = new NavigationItem("documents", "Documents", "\uE8A5", @"C:\Users\a\Documents");
        var home = new NavigationItem("home", "Home", "\uE80F", HomeLocation.Uri);
        var tag = new NavigationItem("tag:1", "Work", "\uE8EC", TagLocation.Uri(1));
        Assert.Contains(SidebarContextAction.WhoLocks, SidebarContextMenu.For(documents));
        Assert.DoesNotContain(SidebarContextAction.WhoLocks, SidebarContextMenu.For(home));
        Assert.DoesNotContain(SidebarContextAction.WhoLocks, SidebarContextMenu.For(tag));
    }
}
