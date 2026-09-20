using FilesMate.Core.Metadata;
using FilesMate.Platform.Windows.Metadata;

namespace FilesMate.Platform.Windows.Tests.Metadata;

public sealed class WindowsFileIdentityProviderTests
{
    [Fact]
    public void Existing_file_uses_a_stable_identity_and_normalized_path()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "identity");
        try
        {
            var identity = new WindowsFileIdentityProvider().Resolve(path);
            Assert.True(identity.IsStable);
            Assert.StartsWith("file:", identity.StableKey, StringComparison.Ordinal);
            Assert.Equal(Path.GetFullPath(path).Replace('/', '\\'), identity.NormalizedPath, ignoreCase: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_or_network_paths_fall_back_to_normalized_path_identity()
    {
        var path = @"C:\FilesMate\missing\..\report.txt";
        var identity = new WindowsFileIdentityProvider().Resolve(path);
        Assert.False(identity.IsStable);
        Assert.Equal(@"path:c:\filesmate\report.txt", identity.StableKey);
        Assert.Equal(@"C:\FilesMate\report.txt", identity.NormalizedPath);
    }
}
