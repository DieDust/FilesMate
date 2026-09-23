using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

public sealed class SafeDeviceEjectTests
{
    [Theory]
    [InlineData(@"USB\Class_ff&SubClass_42&Prot_01", true)]
    [InlineData(@"USB\COMPAT_VID_22d9&Class_ff&SubClass_42&Prot_01", true)]
    [InlineData(@"USB\class_FF&subclass_42&prot_01", true)]
    [InlineData(@"USB\Class_ff&SubClass_42&Prot_03", false)]
    [InlineData(@"USB\Class_06&SubClass_01&Prot_01", false)]
    [InlineData(@"USB\Class_ff&SubClass_42", false)]
    [InlineData(@"PCI\Class_ff&SubClass_42&Prot_01", false)]
    [InlineData("ADB Interface", false)]
    public void Usb_debugging_requires_the_exact_device_interface_protocol(string id, bool expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, SafeDeviceEject.IsAdbCompatibleId(id));
    }

    [Theory]
    [InlineData(@"X:\", true)]
    [InlineData("X:", true)]
    [InlineData(@"X:\folder", false)]
    [InlineData(@"\\server\share", false)]
    [InlineData(@"\\.\PhysicalDrive0", false)]
    [InlineData("", false)]
    public void Removal_requires_a_drive_root(string path, bool valid)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(valid, SafeDeviceEject.IsDriveRoot(path));
    }

    [Fact]
    public async Task System_volume_is_never_offered_for_safe_removal()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(await SafeDeviceEject.QueryAsync(Path.GetPathRoot(Environment.SystemDirectory)!));
    }

    [Fact]
    public async Task Missing_device_and_invalid_targets_cannot_request_hardware_removal()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Null(await SafeDeviceEject.QueryAsync(@"X:\folder"));
        var result = await SafeDeviceEject.RequestAsync(new(@"X:\folder", "untrusted", []));
        Assert.False(result.Succeeded);
    }
}
