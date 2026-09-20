using System.Text.Json;

using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Licensing;

public sealed class FilesCommunityLicenseTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(ThemeXaml.AppRoot, "..", ".."));

    [Fact]
    public void Adapted_files_have_pinned_mit_provenance_and_notice()
    {
        var manifestPath = Path.Combine(RepoRoot, "docs", "upstream", "files-community-ui.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;

        Assert.Equal(
            "abdfcb543adc7fcdf7f75672feb83760c48d9087",
            root.GetProperty("upstreamCommit").GetString());

        var files = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Equal("MIT", file.GetProperty("license").GetString()));

        var notice = File.ReadAllText(Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("Files Community", notice, StringComparison.Ordinal);
        Assert.Contains("MIT License", notice, StringComparison.Ordinal);

        foreach (var file in files)
        {
            var target = file.GetProperty("target").GetString();
            Assert.False(string.IsNullOrWhiteSpace(target));
            var header = File.ReadLines(Path.Combine(RepoRoot, target!)).Take(4);
            Assert.Contains(header, line => line.Contains("Licensed under the MIT License", StringComparison.Ordinal));
        }
    }
}
