using FilesMate.App.Services;
using FilesMate.Platform.Windows.Archives;
using FilesMate.Platform.Windows.CompactMate;

namespace FilesMate.App.Tests.Services;

public sealed class ArchiveRoutingTests
{
    [Fact]
    public void No_external_installation_and_explicit_built_in_never_start_external_software()
    {
        Assert.Equal(ArchiveProvider.BuiltIn, ArchiveRouting.Choose(new(), CompactMateVerb.ExtractHere,
            [@"C:\data\archive.rar"], false, (_, _) => null).Provider);
        Assert.Equal(ArchiveProvider.BuiltIn, ArchiveRouting.Choose(new(ArchiveProvider.BuiltIn), CompactMateVerb.Compress7z,
            [@"C:\data\hello.txt"], false, (_, _) => throw new InvalidOperationException()).Provider);
    }

    [Fact]
    public void Explicit_preference_wins_and_uninstalled_preference_uses_built_in()
    {
        var preference = new ArchivePreferences(ArchiveProvider.Bandizip);
        var selected = ArchiveRouting.Choose(preference, CompactMateVerb.ExtractHere, [@"C:\data\x.zip"], false,
            (provider, _) => provider == ArchiveProvider.Bandizip ? @"D:\Portable apps\Bandizip.exe" : throw new InvalidOperationException());
        Assert.Equal(ArchiveProvider.Bandizip, selected.Provider);
        Assert.False(selected.UsedFallback);
        Assert.True(ArchiveRouting.Choose(preference, CompactMateVerb.ExtractHere, [@"C:\data\x.zip"], false, (_, _) => null).UsedFallback);
    }

    [Theory]
    [InlineData(ArchiveProvider.Zip360)]
    [InlineData(ArchiveProvider.HaoZip)]
    [InlineData(ArchiveProvider.Other)]
    public void Open_only_apps_are_not_given_speculative_extraction_arguments(ArchiveProvider provider)
    {
        Assert.False(ArchiveRouting.Supports(provider, @"C:\app.exe", CompactMateVerb.ExtractHere, [@"C:\x.zip"], false));
        Assert.True(ArchiveRouting.Supports(provider, @"C:\app.exe", CompactMateVerb.Open, [@"C:\x.zip"], false));
        var launch = ArchiveRouting.Create(new(provider, @"C:\app.exe", false), CompactMateVerb.Open, [@"C:\中文 空格\x.zip"], "", "");
        Assert.Equal([@"C:\中文 空格\x.zip"], launch.Arguments);
    }

    [Fact]
    public void Bandizip_batch_and_creation_preserve_arguments_and_conflict_prompts()
    {
        var route = new ArchiveRoute(ArchiveProvider.Bandizip, @"C:\Program Files\Bandizip\Bandizip.exe", false);
        string[] inputs = [@"C:\中文 路径\a.zip", @"D:\other\b.7z"];
        var extract = ArchiveRouting.Create(route, CompactMateVerb.ExtractToFolder, inputs, @"D:\输出 空格", "unused");
        Assert.Equal(["bx", @"-o:D:\输出 空格", "-target:name", .. inputs], extract.Arguments);
        var create = ArchiveRouting.Create(route, CompactMateVerb.Compress7z, inputs, @"D:\输出 空格", "资料.7z");
        Assert.Equal("cd", create.Arguments[0]);
        Assert.Contains("-fmt:7z", create.Arguments);
        Assert.DoesNotContain("-y", extract.Arguments);
        Assert.DoesNotContain("-aoa", extract.Arguments);
    }

    [Fact]
    public void Renamed_volumes_and_recursive_smart_operations_use_capability_fallback()
    {
        Assert.True(ArchiveRouting.Choose(new(ArchiveProvider.Bandizip), CompactMateVerb.ExtractHere,
            [@"D:\x.7z.001删"], true, (_, _) => @"C:\Bandizip.exe").UsedFallback);
        Assert.True(ArchiveRouting.Choose(new(ArchiveProvider.WinRAR), CompactMateVerb.Compress7z,
            [@"D:\hello.txt"], false, (_, _) => @"C:\WinRAR.exe").UsedFallback);
        Assert.False(ArchiveRouting.Supports(ArchiveProvider.SevenZip, @"C:\7z.exe", CompactMateVerb.ExtractHere, [@"C:\x.zip"], false));
        Assert.True(ArchiveRouting.Supports(ArchiveProvider.SevenZipCompatible, @"C:\portable\7zG.exe", CompactMateVerb.ExtractHere, [@"C:\x.zip"], false));
    }

    [Fact]
    public void Preferences_round_trip_portable_path_and_recover_from_invalid_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-preferences-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "archives.json");
        try
        {
            var store = new ArchivePreferencesStore(file);
            Assert.Equal(ArchiveProvider.Automatic, store.Load().Preferred);
            store.Save(new(ArchiveProvider.Bandizip, new() { [ArchiveProvider.Bandizip] = @"D:\中文 便携版\Bandizip.exe" }));
            Assert.Equal(ArchiveProvider.Bandizip, store.Load().Preferred);
            Assert.Equal(@"D:\中文 便携版\Bandizip.exe", store.Load().PathFor(ArchiveProvider.Bandizip));
            File.WriteAllText(file, "broken json");
            Assert.Equal(ArchiveProvider.Automatic, store.Load().Preferred);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
