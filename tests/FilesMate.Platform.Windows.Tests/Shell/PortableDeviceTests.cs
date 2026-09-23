using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class PortableDeviceTests
{
    private static PortableDeviceLocation Root => new(
        PortableDeviceLocation.ComputerPrefix + @"\\?\usb#test#" + PortableDeviceLocation.InterfaceId, "测试 iPad", []);

    [Fact]
    public void Device_locations_round_trip_without_treating_opaque_ids_as_disk_paths()
    {
        var root = Root;
        var storage = root.Child("Internal Storage", root.Root + @"\SID-{storage}");
        var folder = storage.Child("写真", storage.ParsingName + @"\{folder}");
        Assert.True(PortableDeviceLocation.TryParse(folder.Uri, out var restored));
        Assert.Equal(folder.Uri, restored.Uri);
        Assert.Equal("测试 iPad › Internal Storage › 写真", restored.Address);
        Assert.Equal(storage.Uri, restored.Parent!.Uri);
        Assert.Equal(root.Uri, restored.Parent.Parent!.Uri);
        Assert.Null(restored.Parent.Parent.Parent);
    }

    [Theory]
    [InlineData("C:\\test")]
    [InlineData("filesmate-device:invalid")]
    [InlineData("filesmate-device:bnVsbA")]
    [InlineData("shell:ControlPanelFolder")]
    public void Invalid_locations_are_rejected(string uri) => Assert.False(PortableDeviceLocation.TryParse(uri, out _));

    [Fact]
    public void Disk_roots_and_unrelated_children_are_rejected()
    {
        Assert.False(PortableDeviceLocation.TryParse(new PortableDeviceLocation(@"C:\", "disk", []).Uri, out _));
        Assert.Throws<ArgumentException>(() => Root.Child("fake", @"C:\file"));
        Assert.Throws<ArgumentException>(() => Root.Child("fake", Root.Root + @"\child\outside"));
        Assert.False(PortableDeviceLocation.TryParse((Root with { Segments = [new("fake", @"C:\file")] }).Uri, out _));
    }

    [Fact]
    public async Task Canceled_device_reads_do_not_enter_the_driver()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var read = PortableDeviceService.ReadFolderAsync(Root, cancelled.Token);
        var list = PortableDeviceService.GetDevicesAsync(cancelled.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => list);
    }

    [Fact]
    public async Task Concurrent_shell_browsing_and_preview_reject_a_missing_device_cleanly()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (var repetition = 0; repetition < 4; repetition++)
        {
            var devices = PortableDeviceService.GetDevicesAsync(timeout.Token);
            var metadata = PortableDeviceService.GetDetailsAsync(Root, timeout.Token);
            var details = Assert.ThrowsAsync<DirectoryNotFoundException>(() => metadata);
            var thumbnail = PortableDeviceService.GetThumbnailAsync(Root, 96, timeout.Token);
            await Task.WhenAll(devices, details, thumbnail);
            Assert.Null(await thumbnail);
            Assert.DoesNotContain(await devices, d => d.Root == Root.Root);
        }
    }

    [Theory]
    [InlineData(0x219, 7, true)]
    [InlineData(0x219, 0x8000, true)]
    [InlineData(0x219, 0x8004, true)]
    [InlineData(0x219, 0x8001, false)]
    [InlineData(0x5, 0x8004, false)]
    public void Only_device_changes_trigger_refresh(uint message, uint change, bool expected) =>
        Assert.Equal(expected, DeviceChangeSubscription.IsRefreshMessage(message, change));

    [Theory]
    [InlineData(@"E:\child")]
    [InlineData(@"\\server\share")]
    [InlineData("shell:RecycleBinFolder")]
    public void Eject_does_not_execute_non_drive_paths(string path) => Assert.Throws<ArgumentException>(() => DriveShell.EjectStartInfo(path));
}
