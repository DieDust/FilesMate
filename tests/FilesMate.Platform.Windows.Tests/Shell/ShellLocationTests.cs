using FilesMate.Platform.Windows.Shell;

namespace FilesMate.Platform.Windows.Tests.Shell;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ShellLocationTests
{
    [Theory]
    [InlineData("shell:Personal")]
    [InlineData("shell:Downloads")]
    [InlineData("shell:Startup")]
    public async Task KnownFoldersResolveToRealPaths(string alias)
    {
        var result = await ShellLocation.ResolveFileSystemPathAsync(alias);
        Assert.NotNull(result);
        Assert.True(Path.IsPathFullyQualified(result));
    }

    [Fact]
    public async Task RecycleBinIsVirtual() => Assert.Null(await ShellLocation.ResolveFileSystemPathAsync("shell:RecycleBinFolder"));
}
