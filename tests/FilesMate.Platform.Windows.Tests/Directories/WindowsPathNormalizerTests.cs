using FilesMate.Platform.Windows.Paths;

namespace FilesMate.Platform.Windows.Tests.Directories;

public sealed class WindowsPathNormalizerTests
{
    private readonly WindowsPathNormalizer _normalizer = new();

    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\", @"C:/")]
    [InlineData(@"D:\Files", @"D:\Files\")]
    [InlineData(@"D:\Files\sub", @"D:\Files\sub\.")]
    [InlineData(@"D:\Files", @"D:\Files\sub\..")]
    [InlineData(@"C:\", @"C:\Windows\..")]
    [InlineData(@"\\server\share", @"\\server\share\")]
    [InlineData(@"\\server\share\docs", @"\\server\share\docs\.")]
    [InlineData(@"\\server\share", @"\\server\share\docs\..")]
    [InlineData(@"\\?\C:\Long", @"\\?\C:\Long\")]
    [InlineData(@"\\?\C:\Long", @"\\?\C:\Long\.\inner\..")]
    public void Normalize_resolves_dots_without_escaping_the_root(string expected, string input)
    {
        Assert.Equal(expected, _normalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_preserves_user_visible_casing()
    {
        Assert.Equal(@"C:\Windows\System32", _normalizer.Normalize(@"C:\Windows\System32"));
        Assert.Equal(@"C:\Windows\System32", _normalizer.Normalize(@"C:\Windows\System32\"));
        Assert.NotEqual(@"c:\windows\system32", _normalizer.Normalize(@"C:\Windows\System32"));
    }

    [Fact]
    public void Equals_follows_case_insensitive_volume_rules()
    {
        Assert.True(_normalizer.Equals(@"C:\Windows", @"c:\windows"));
        Assert.True(_normalizer.Equals(@"\\Server\Share\Docs", @"\\server\share\docs"));
        Assert.False(_normalizer.Equals(@"C:\Windows", @"C:\Users"));
    }

    [Fact]
    public void Normalize_strips_surrounding_quotes()
    {
        Assert.Equal(@"C:\Windows\notepad.exe", _normalizer.Normalize("\"C:\\Windows\\notepad.exe\""));
        Assert.Equal(@"C:\Windows", _normalizer.Normalize("\"C:\\Windows\""));
    }

    [Fact]
    public void Normalize_rejects_empty_relative_and_null_embedded_paths()
    {
        Assert.Throws<ArgumentException>(() => _normalizer.Normalize(""));
        Assert.Throws<ArgumentException>(() => _normalizer.Normalize("   "));
        Assert.Throws<ArgumentException>(() => _normalizer.Normalize(@"relative\path"));
        Assert.Throws<ArgumentException>(() => _normalizer.Normalize("C:\\foo\0bar"));
    }

    [Theory]
    [InlineData(@"D:\a\b", @"D:\a")]
    [InlineData(@"D:\a", @"D:\")]
    [InlineData(@"\\server\share\docs", @"\\server\share")]
    public void GetParent_stops_at_the_volume_or_share_root(string path, string expected)
    {
        Assert.Equal(expected, _normalizer.GetParent(path));
    }

    [Fact]
    public void GetParent_returns_null_at_a_drive_or_share_root()
    {
        Assert.Null(_normalizer.GetParent(@"D:\"));
        Assert.Null(_normalizer.GetParent(@"Z:"));
        Assert.Null(_normalizer.GetParent(@"\\server\share"));
    }

    [Fact]
    public void Normalize_expands_environment_variables_and_home_shorthand()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(local));
        Assert.False(string.IsNullOrEmpty(home));

        Assert.Equal(_normalizer.Normalize(local), _normalizer.Normalize(@"%LOCALAPPDATA%"));
        Assert.Equal(_normalizer.Normalize(local), _normalizer.Normalize(@"%localappdata%\"));
        Assert.Equal(
            _normalizer.Normalize(Path.Combine(local, "EpicGamesLauncher", "Saved")),
            _normalizer.Normalize(@"%LocalAppData%\EpicGamesLauncher\Saved\"));
        Assert.Equal(_normalizer.Normalize(home), _normalizer.Normalize("~"));
        Assert.Equal(
            _normalizer.Normalize(Path.Combine(home, "Downloads")),
            _normalizer.Normalize(@"~\Downloads"));
    }

    [Fact]
    public void Normalize_mapped_drive_root_keeps_trailing_separator()
    {
        Assert.Equal(@"Z:\", _normalizer.Normalize(@"Z:"));
        Assert.Equal(@"Z:\", _normalizer.Normalize(@"Z:\"));
        Assert.Equal(@"Z:\mapped", _normalizer.Normalize(@"Z:\mapped\"));
    }
}
