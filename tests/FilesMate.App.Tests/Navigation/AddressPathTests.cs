using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class AddressPathTests
{
    [Theory]
    [InlineData("..", @"D:\Projects\Demo", @"D:\Projects")]
    [InlineData(@".\docs", @"D:\Projects\Demo", @"D:\Projects\Demo\docs")]
    [InlineData("../Other", @"D:\Projects\Demo", @"D:\Projects\Other")]
    [InlineData("notes.txt", @"\\server\share\docs", @"\\server\share\docs\notes.txt")]
    [InlineData("file:///D:/My%20Files/report.txt", @"C:\", @"D:\My Files\report.txt")]
    [InlineData(@"""C:\My Files""", @"D:\", @"C:\My Files")]
    public void Resolves_input_against_the_pane(string input, string current, string expected) =>
        Assert.Equal(expected, AddressPath.Normalize(input, current));

    [Fact]
    public void Virtual_locations_require_absolute_input()
    {
        Assert.Throws<ArgumentException>(() => AddressPath.Normalize("docs", HomeLocation.Uri));
        Assert.Throws<ArgumentException>(() => AddressPath.Normalize("..", TagLocation.Uri(1)));
        Assert.Equal(@"D:\Docs", AddressPath.Normalize(@"D:\Docs", HomeLocation.Uri));
        Assert.Equal(HomeLocation.Uri, AddressPath.Normalize(HomeLocation.Uri, @"D:\"));
    }

    [Fact]
    public void Expands_home_and_environment_before_resolving_relative_paths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, AddressPath.Normalize("~", @"D:\"));
        Assert.Equal(home, AddressPath.Normalize("%USERPROFILE%", @"D:\"));
        Assert.Equal(Path.Combine(home, "Documents"), AddressPath.Normalize("~/Documents", @"D:\"));
    }
}
