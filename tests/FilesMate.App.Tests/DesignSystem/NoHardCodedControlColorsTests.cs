using System.Text.RegularExpressions;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class NoHardCodedControlColorsTests
{
    private static readonly Regex HexColor = new(
        @"#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b",
        RegexOptions.Compiled);

    [Fact]
    public void Control_xaml_does_not_introduce_hex_colors()
    {
        var controlsRoot = Path.Combine(ThemeXaml.AppRoot, "Controls");
        Assert.True(Directory.Exists(controlsRoot), $"Missing controls directory: {controlsRoot}");

        var offenders = Directory.EnumerateFiles(controlsRoot, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => HexColor.Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(ThemeXaml.AppRoot, path)}:{match.Value}"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Controls XAML must use ThemeResource tokens, not hex colors:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Theme_dictionaries_do_not_hard_code_hex_colors()
    {
        var offenders = Directory.EnumerateFiles(ThemeXaml.ThemesRoot, "*.xaml", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "AppThemeResources.xaml", StringComparison.Ordinal))
            .SelectMany(path => HexColor.Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetFileName(path)}:{match.Value}"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Theme dictionaries other than AppThemeResources.xaml must alias tokens, not hex colors:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }
}
