using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class LanguageRestartSessionTests
{
    [Fact]
    public void Handoff_preserves_multiple_windows_duplicate_tabs_and_selection_without_overwriting_normal_session()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMateLanguageRestart", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "window-session.json"), "original session");
            var expected = new LanguageRestartSession(123, 456,
                [new(["C:\\first", "D:\\日本語", "C:\\first"], 2), new(["home://"], 0)]);
            var token = expected.Save(directory);
            var restored = LanguageRestartSession.Load(directory, token);
            Assert.Equal(expected.ParentPid, restored.ParentPid);
            Assert.Equal(expected.ParentStartedUtcTicks, restored.ParentStartedUtcTicks);
            Assert.Equal(2, restored.Windows.Length);
            Assert.Equal(expected.Windows[0].Tabs, restored.Windows[0].Tabs);
            Assert.Equal(2, restored.Windows[0].SelectedTabIndex);
            Assert.Equal(expected.Windows[1].Tabs, restored.Windows[1].Tabs);
            Assert.Equal("original session", File.ReadAllText(Path.Combine(directory, "window-session.json")));
            Assert.NotEqual(token, expected.Save(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("../settings")]
    [InlineData("C:\\settings")]
    [InlineData("")]
    public void Handoff_rejects_non_token_paths(string token) =>
        Assert.Throws<ArgumentException>(() => LanguageRestartSession.FilePath(Path.GetTempPath(), token));
}
