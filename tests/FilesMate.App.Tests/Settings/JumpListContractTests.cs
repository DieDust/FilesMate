using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Settings;

public sealed class JumpListContractTests
{
    [Fact]
    public void Recent_folders_use_the_default_app_icon_instead_of_an_invalid_file_uri()
    {
        var source = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Services", "JumpListService.cs"));
        Assert.DoesNotContain("item.Logo =", source, StringComparison.Ordinal);
        Assert.Contains("jumpList.Items.Add(item)", source, StringComparison.Ordinal);
        Assert.Contains("jumpList.SaveAsync()", source, StringComparison.Ordinal);
    }
}
