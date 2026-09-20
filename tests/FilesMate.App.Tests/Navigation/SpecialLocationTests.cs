using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class SpecialLocationTests
{
    [Theory]
    [InlineData("回收站", "shell:RecycleBinFolder")]
    [InlineData("Recycle Bin", "shell:RecycleBinFolder")]
    [InlineData("下载", "shell:Downloads")]
    [InlineData("文档", "shell:Personal")]
    [InlineData("网络", "shell:NetworkPlacesFolder")]
    [InlineData("shell:Startup", "shell:Startup")]
    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", "::{645FF040-5081-101B-9F08-00AA002F954E}")]
    public void RecognizesExplicitSystemLocations(string text, string expected) => Assert.Equal(expected, SpecialLocation.ShellName(text));

    [Theory]
    [InlineData(@"D:\文档")]
    [InlineData("wt -d anything")]
    [InlineData("powershell -Command anything")]
    public void DoesNotInterpretPathsOrCommands(string text) => Assert.Null(SpecialLocation.ShellName(text));
}
