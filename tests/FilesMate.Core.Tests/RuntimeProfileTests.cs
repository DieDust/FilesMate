using FilesMate.Core.Configuration;

namespace FilesMate.Core.Tests;

public sealed class RuntimeProfileTests
{
    [Fact]
    public void OrdinaryLaunchKeepsPathsArgumentsAndInstanceIdentity()
    {
        var profile = ProfileContext.Parse(["FilesMate.App.exe", "/open", @"D:\fixture"]);
        Assert.False(profile.IsIsolated);
        Assert.Equal(ProfileContext.OrdinaryDirectory, profile.DirectoryPath);
        Assert.Equal("FilesMate.Main", profile.ScopedName("FilesMate.Main"));
        Assert.Equal(@"C:\unrelated\settings.json", profile.MapSettingsPath(@"C:\unrelated\settings.json"));
        Assert.Equal(new[] { "/open", @"D:\fixture" }, profile.ForChild(["/open", @"D:\fixture"]));
    }

    [Theory]
    [InlineData("--test-profile")]
    [InlineData("--test-profile", "relative")]
    [InlineData("--test-profile", "C:\\profile")]
    [InlineData("--test-profile", "D:\\WpSystem\\original")]
    [InlineData("--test-profile", "D:\\FilesMate\\artifacts\\..\\original")]
    [InlineData("--test-profile", "D:\\FilesMate\\artifacts\\test:stream")]
    public void InvalidProfileFailsBeforeFallback(params string[] args) => Assert.Throws<ArgumentException>(() => ProfileContext.Parse(args));

    [Fact]
    public void DuplicateProfileFailsClosed() => Assert.Throws<ArgumentException>(() => ProfileContext.Parse([
        "--test-profile", @"D:\FilesMate\artifacts\test-a", "--test-profile", @"D:\FilesMate\artifacts\test-b"]));

    [Fact]
    public void NestedCachesStayNestedAndOutsideSettingsAreRejected()
    {
        var profile = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-unit");
        Assert.Equal(Path.Combine(profile.DirectoryPath, "Updates", "cache"),
            profile.MapSettingsPath(Path.Combine(ProfileContext.OrdinaryDirectory, "Updates", "cache")));
        Assert.Equal(Path.Combine(profile.DirectoryPath, "metadata.db"), profile.MapSettingsPath(Path.Combine(profile.DirectoryPath, "metadata.db")));
        Assert.Throws<ArgumentException>(() => profile.MapSettingsPath(@"C:\outside\metadata.db"));
    }

    [Fact]
    public void ChildrenRetainProfileOnceWithoutBecomingALaunchTarget()
    {
        var profile = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-unit");
        var child = profile.ForChild(["--search-action", "fixture-id", "--test-profile", profile.DirectoryPath]);
        Assert.Equal(new[] { "--search-action", "fixture-id", "--test-profile", profile.DirectoryPath }, child);
        Assert.Equal(new[] { "--search-action", "fixture-id" }, ProfileContext.WithoutProfileArguments(child));
    }

    [Fact]
    public void InstanceAndIncomingEventAreStableWithinOneProfileAndDistinctAcrossProfiles()
    {
        var a = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-a");
        var same = new ProfileContext(@"d:\filesmate\artifacts\test-profile-a");
        var b = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-b");
        foreach (var ordinary in new[] { "FilesMate.Main", @"Local\FilesMate.IncomingLaunch" })
        {
            Assert.Equal(a.ScopedName(ordinary), same.ScopedName(ordinary));
            Assert.NotEqual(a.ScopedName(ordinary), b.ScopedName(ordinary));
            Assert.NotEqual(ordinary, a.ScopedName(ordinary));
        }
    }

    [Theory]
    [InlineData("FilesMate.App.exe")]
    [InlineData("filesmate.app.exe")]
    [InlineData("FilesMate.SearchHost.exe")]
    [InlineData("FILESMATE.SEARCHHOST.EXE")]
    public void EveryWindowsCasingOfOwnChildRetainsTheProfile(string name)
    {
        var profile = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-unit");
        Assert.Equal(new[] { "--background", "--test-profile", profile.DirectoryPath },
            profile.ForExecutable(Path.Combine(@"D:\ordinary-product", name), ["--background"]));
    }

    [Fact]
    public void OtherApplicationsReceiveTheirOriginalArguments()
    {
        var profile = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-unit");
        Assert.Equal(new[] { @"D:\fixture\file.txt" }, profile.ForExecutable(@"D:\Editor\editor.exe", [@"D:\fixture\file.txt"]));
    }

    [Theory]
    [InlineData("--office-preview", 3)]
    [InlineData("--native-office-preview", 5)]
    public void PreviewWorkerArgumentsRoundTripWithoutLosingProfileOrPayload(string command, int count)
    {
        var arguments = Enumerable.Range(0, count).Select(i => i == 0 ? command : $"preview payload {i}").ToArray();
        var executable = @"D:\ordinary-product\SearchHost\FilesMate.SearchHost.exe";
        var ordinary = new ProfileContext(null);
        Assert.Equal(arguments, ordinary.ForExecutable(executable, arguments));
        var isolated = new ProfileContext(@"D:\FilesMate\artifacts\test-profile-unit");
        var child = isolated.ForExecutable(executable, arguments);
        Assert.Equal(arguments, ProfileContext.WithoutProfileArguments(child));
        Assert.Equal(isolated.DirectoryPath, ProfileContext.Parse(child).DirectoryPath);
        Assert.Equal(count + 2, child.Length);
    }
}
