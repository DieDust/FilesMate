using System.Xml.Linq;
using FilesMate.App.Localization;
using FilesMate.App.Models;

namespace FilesMate.App.Tests.Services;

public sealed class SettingsSearchTests
{
    [Theory]
    [InlineData("双击", "Opening_FileTitle")]
    [InlineData("文件夹 打开", "Opening_FolderTitle")]
    [InlineData("single click", "Opening_FileTitle")]
    [InlineData("标签", "SettingsTags")]
    [InlineData("Alt Space", "SearchShortcut")]
    [InlineData("隐藏", "ShowHiddenFilesTitle")]
    public void Search_finds_the_relevant_setting(string query, string title)
        => Assert.Contains(SettingsSearchCatalog.Search(query), entry => entry.TitleKey == title);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-such-setting-xyz")]
    public void Empty_and_unmatched_queries_have_no_suggestions(string query)
        => Assert.Empty(SettingsSearchCatalog.Search(query));

    [Fact]
    public void Results_have_translated_titles_and_resolvable_targets()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "FilesMate.App"))) root = root.Parent;
        Assert.NotNull(root);
        var pages = new Dictionary<string, string> { ["general"] = "GeneralPage", ["appearance"] = "AppearancePage",
            ["files-folders"] = "FilesAndFoldersSettingsPage", ["search"] = "SearchSettingsPage", ["keyboard"] = "ShortcutsSettingsPage",
            ["tags"] = "TagManagementPage", ["advanced"] = "AdvancedSettingsPage", ["about"] = "AboutPage" };
        foreach (var entry in SettingsSearchCatalog.Entries)
        {
            Assert.True(StringTable.Additional.ContainsKey(entry.TitleKey) || StringTable.English.ContainsKey(entry.TitleKey), entry.TitleKey);
            Assert.False(string.IsNullOrEmpty(entry.TargetName));
            var doc = XDocument.Load(Path.Combine(root!.FullName, "src", "FilesMate.App", "Views", pages[entry.Category] + ".xaml"));
            Assert.Contains(doc.Descendants(), node => (string?)node.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == entry.TargetName);
        }
    }
}
