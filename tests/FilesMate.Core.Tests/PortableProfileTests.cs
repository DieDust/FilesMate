using FilesMate.Core.Configuration;

namespace FilesMate.Core.Tests;

public sealed class PortableProfileTests
{
    [Fact]
    public void MarkerlessCopyRetainsOrdinarySettingsAndIdentity()
    {
        using var f = new Fixture();
        var context = ProfileContext.Parse([], f.App);
        Assert.False(context.HasPrivateProfile);
        Assert.Equal(ProfileContext.OrdinaryDirectory, context.DirectoryPath);
        Assert.Equal("FilesMate.Main", context.ScopedName("FilesMate.Main"));
    }

    [Theory]
    [InlineData("first copy")]
    [InlineData("\u4fbf\u643a\u7248 \u6d4b\u8bd5")]
    public void AppAndAdjacentSearchHostResolveTheSamePortableData(string folder)
    {
        using var f = new Fixture(folder);
        f.MarkPortable();
        var app = ProfileContext.Parse(["/open", f.Root], f.App);
        var host = ProfileContext.Parse(["--background"], f.Host);
        Assert.True(app.IsPortable);
        Assert.False(app.IsIsolated);
        Assert.Equal(Path.Combine(f.Root, "data"), app.DirectoryPath);
        Assert.Equal(app.DirectoryPath, host.DirectoryPath);
        Assert.Equal(app.ScopedName("FilesMate.Main"), host.ScopedName("FilesMate.Main"));
        Assert.NotEqual("FilesMate.Main", app.ScopedName("FilesMate.Main"));
        Assert.Equal(new[] { "--background" }, app.ForExecutable(f.Host, ["--background"]));
    }

    [Fact]
    public void IndependentExtractionRootsHaveIndependentProcessIdentities()
    {
        using var a = new Fixture("copy a");
        using var b = new Fixture("copy b");
        a.MarkPortable(); b.MarkPortable();
        var first = ProfileContext.Parse([], a.App);
        var second = ProfileContext.Parse([], b.App);
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
        Assert.NotEqual(first.ScopedName("FilesMate.Main"), second.ScopedName("FilesMate.Main"));
        Assert.NotEqual(first.ScopedName(@"Local\FilesMate.IncomingLaunch"), second.ScopedName(@"Local\FilesMate.IncomingLaunch"));
    }

    [Fact]
    public void PortableStoragePersistsAndMovesWithDataWithoutTouchingOrdinarySettings()
    {
        using var a = new Fixture("original");
        using var b = new Fixture("new location");
        a.MarkPortable(); b.MarkPortable();
        var first = ProfileContext.Parse([], a.App);
        first.PreparePortableStorage();
        var file = first.MapSettingsPath(Path.Combine(ProfileContext.OrdinaryDirectory, "nested", "settings.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"setting\":\"retained\"}");
        var restarted = ProfileContext.Parse([], a.App);
        restarted.PreparePortableStorage();
        Assert.Equal("{\"setting\":\"retained\"}", File.ReadAllText(restarted.MapSettingsPath(
            Path.Combine(ProfileContext.OrdinaryDirectory, "nested", "settings.json"))));
        Directory.Move(first.DirectoryPath, Path.Combine(b.Root, "data"));
        var moved = ProfileContext.Parse([], b.App);
        moved.PreparePortableStorage();
        Assert.Equal("{\"setting\":\"retained\"}", File.ReadAllText(moved.MapSettingsPath(
            Path.Combine(ProfileContext.OrdinaryDirectory, "nested", "settings.json"))));
        Assert.Throws<ArgumentException>(() => moved.MapSettingsPath(Path.Combine(a.Root, "foreign.json")));
    }

    [Fact]
    public void InvalidMarkerFailsWithoutSelectingAnOrdinaryProfile()
    {
        using var f = new Fixture();
        File.WriteAllText(Path.Combine(f.Root, ProfileContext.PortableMarkerName), "foreign");
        Assert.Throws<IOException>(() => ProfileContext.Parse([], f.App));
    }

    [Fact]
    public void UnownedNonemptyDataIsRefusedAndPreserved()
    {
        using var f = new Fixture(); f.MarkPortable();
        Directory.CreateDirectory(Path.Combine(f.Root, "data"));
        var file = Path.Combine(f.Root, "data", "keep.txt");
        File.WriteAllText(file, "user fixture");
        var context = ProfileContext.Parse([], f.App);
        Assert.Throws<IOException>(() => context.PreparePortableStorage());
        Assert.Equal("user fixture", File.ReadAllText(file));
        Assert.False(File.Exists(Path.Combine(context.DirectoryPath, ProfileContext.PortableDataMarkerName)));
    }

    [Fact]
    public void DataPathThatIsAFileFailsWithoutFallback()
    {
        using var f = new Fixture(); f.MarkPortable();
        File.WriteAllText(Path.Combine(f.Root, "data"), "keep");
        Assert.Throws<ArgumentException>(() => ProfileContext.Parse([], f.App));
    }

    [Fact]
    public void LockedOwnershipFileFailsWithoutFallbackOrOverwritingIt()
    {
        using var f = new Fixture(); f.MarkPortable();
        var context = ProfileContext.Parse([], f.App);
        context.PreparePortableStorage();
        var marker = Path.Combine(context.DirectoryPath, ProfileContext.PortableDataMarkerName);
        using (var locked = new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => context.PreparePortableStorage());
        Assert.Equal(ProfileContext.PortableDataMarkerContent, File.ReadAllText(marker));
        Assert.NotEqual(ProfileContext.OrdinaryDirectory, context.DirectoryPath);
    }

    [Fact]
    public async Task ConcurrentColdStartsRetainOneOwnedProfile()
    {
        using var f = new Fixture(); f.MarkPortable();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            ProfileContext.Parse([], f.App).PreparePortableStorage())));
        Assert.Equal(ProfileContext.PortableDataMarkerContent,
            File.ReadAllText(Path.Combine(f.Root, "data", ProfileContext.PortableDataMarkerName)));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.Combine(f.Root, "data")));
    }

    [Fact]
    public void PortableCannotBeConvertedToATestProfileByArguments()
    {
        using var f = new Fixture(); f.MarkPortable();
        Assert.Throws<ArgumentException>(() => ProfileContext.Parse(
            ["--test-profile", @"D:\FilesMate\artifacts\portable-test"], f.App));
    }

    [Fact]
    public void InstallerUpdateGuardRejectsPortableAndPreservesOrdinaryPolicy()
    {
        new ProfileContext(null).RequireInstallerUpdates();
        var context = new ProfileContext(null, Path.Combine(Path.GetTempPath(), "portable-profile"));
        var error = Assert.Throws<InvalidOperationException>(() => context.RequireInstallerUpdates());
        Assert.Contains("ZIP", error.Message);
        Assert.Contains("data", error.Message);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string TestBase = Path.Combine(Path.GetTempPath(), "FilesMate.PortableProfileTests");
        internal string Root { get; }
        internal string App => Path.Combine(Root, "FilesMate.App.exe");
        internal string Host => Path.Combine(Root, "SearchHost", "FilesMate.SearchHost.exe");
        internal Fixture(string folder = "fixture")
        {
            Root = Path.Combine(TestBase, Guid.NewGuid().ToString("N"), folder);
            Directory.CreateDirectory(Path.GetDirectoryName(Host)!);
        }
        internal void MarkPortable() => File.WriteAllText(Path.Combine(Root, ProfileContext.PortableMarkerName),
            ProfileContext.PortableMarkerContent);
        public void Dispose()
        {
            var target = Path.GetDirectoryName(Root)!;
            if (!ProfileContext.IsInside(Path.GetFullPath(target), Path.GetFullPath(TestBase)))
                throw new InvalidOperationException("Fixture cleanup escaped its test root.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
