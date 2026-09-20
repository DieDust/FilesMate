using FilesMate.Platform.Windows.Associations;
using System.Runtime.Versioning;

namespace FilesMate.Platform.Windows.Tests.Associations;

[SupportedOSPlatform("windows")]
public sealed class SpecialLocationLaunchTests
{
    [Theory]
    [InlineData("shell:RecycleBinFolder")]
    [InlineData("shell:Libraries")]
    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}")]
    public void VirtualLocationsUseSeparateRootedExplorerWithoutShellDispatch(string location)
    {
        if (!OperatingSystem.IsWindows()) return;
        var start = ClassicExplorer.CreateLocationStartInfo(location);
        Assert.Equal(ClassicExplorer.ExecutablePath, start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal("/n,/separate,/root," + location, Assert.Single(start.ArgumentList));
    }

    [Theory]
    [InlineData("C:\\Windows")]
    [InlineData("shell:Libraries,/select,C:\\Windows")]
    [InlineData("shell:Libraries\" /select,C:\\Windows")]
    public void RejectsNonNamespaceAndAdditionalExplorerArguments(string location)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => ClassicExplorer.CreateLocationStartInfo(location));
    }
}
