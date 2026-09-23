using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PortableDeviceTransferTests
{
    [Theory]
    [InlineData(0x20000100u, true)]
    [InlineData(0x20040100u, false)]
    [InlineData(0x20000000u, false)]
    [InlineData(0x100u, false)]
    public void Receiving_requires_a_writable_folder_drop_target(uint attributes, bool expected) =>
        Assert.Equal(expected, PortableDeviceService.CanReceive(attributes));

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 0, false)]
    [InlineData(0x00270008, 1, true)]
    [InlineData(0x00270005, 1, false)] // COPYENGINE_S_USER_IGNORED
    [InlineData(0x00270006, 1, false)] // COPYENGINE_S_MERGE (folder still needs its children)
    [InlineData(unchecked((int)0x800704C7), 0, false)]
    public void Skipped_or_unpublished_items_are_not_reported_as_copied(int hr, int created, bool expected) =>
        Assert.Equal(expected, PortableDeviceService.IsCopyCompleted(hr, created));

    [Theory]
    [InlineData("shell:ControlPanelFolder")]
    [InlineData("https://example.com/file")]
    [InlineData("filesmate-device:invalid")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\")]
    [InlineData("relative.txt")]
    public void Transfer_rejects_arbitrary_shell_and_device_namespace_paths(string address) =>
        Assert.Throws<ArgumentException>(() => PortableDeviceService.ValidateTransferLocation(address));

    [Fact]
    public void Copy_does_not_enable_automatic_replacement_or_rename()
    {
        Assert.Equal(0u, PortableDeviceService.CopyFlags & (0x10u | 0x8u | 0x400000u));
    }

    [Fact]
    public async Task Shell_copy_transfers_files_and_nested_folders_without_removing_sources()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var source = Directory.CreateDirectory(Path.Combine(fixture.Root, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(fixture.Root, "destination")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(source, "日本語 中文", "nested")).FullName;
        var bytes = Enumerable.Range(0, 65537).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(source, "sample.bin"), bytes);
        await File.WriteAllTextAsync(Path.Combine(folder, "测试.txt"), "transfer integrity");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await PortableDeviceService.CopyAsync([Path.Combine(source, "sample.bin"), Path.GetDirectoryName(folder)!], destination, token: timeout.Token);
        Assert.True(result.Succeeded, result.ToString());
        Assert.True(result.Completed >= 2);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(destination, "sample.bin")));
        Assert.Equal("transfer integrity", await File.ReadAllTextAsync(Path.Combine(destination, "日本語 中文", "nested", "测试.txt")));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(source, "sample.bin")));
        Assert.True(File.Exists(Path.Combine(folder, "测试.txt")));
    }

    [Fact]
    public async Task Cancelled_copy_does_not_create_or_remove_any_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Root, "keep.txt");
        await File.WriteAllTextAsync(source, "keep");
        var destination = Directory.CreateDirectory(Path.Combine(fixture.Root, "destination")).FullName;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PortableDeviceService.CopyAsync([source], destination, token: cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
        Assert.Equal("keep", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task Missing_source_prevents_the_whole_batch_from_starting()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture();
        var existing = Path.Combine(fixture.Root, "keep.txt");
        await File.WriteAllTextAsync(existing, "keep");
        var destination = Directory.CreateDirectory(Path.Combine(fixture.Root, "destination")).FullName;
        await Assert.ThrowsAnyAsync<Exception>(() => PortableDeviceService.CopyAsync([existing, Path.Combine(fixture.Root, "missing.txt")], destination));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public void Folder_merge_does_not_hide_skipped_children_when_later_files_succeed()
    {
        var sink = new PortableDeviceService.CopySink(CancellationToken.None);
        sink.PostCopyItem(0, 0, 0, 0, 0x00270006, 1); // merge destination directory
        sink.PostCopyItem(0, 0, 0, 0, 0x00270005, 0); // user skips one child
        sink.PostCopyItem(0, 0, 0, 0, 0, 1); // a later child succeeds
        Assert.Equal(1, sink.Completed);
        Assert.Equal(1, sink.Incomplete);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "FilesMate-device-copy-" + Guid.NewGuid().ToString("N"))).FullName;
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
