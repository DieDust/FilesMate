using FilesMate.App.Localization;
using FilesMate.App.Tests.DesignSystem;

namespace FilesMate.App.Tests.Localization;

public sealed class StatusLocalizationTests
{
    [Fact]
    public void Counts_and_session_states_have_complete_english_and_chinese_formats()
    {
        Assert.Equal("{0} items", StringTable.English["Status_Items"]);
        Assert.Equal("{0} items · loading", StringTable.English["Status_ItemsLoading"]);
        Assert.Equal("{0} selected", StringTable.English["Status_Selected"]);

        Assert.Equal("{0} 个项目", StringTable.Chinese["Status_Items"]);
        Assert.Equal("{0} 个项目 · 正在加载", StringTable.Chinese["Status_ItemsLoading"]);
        Assert.Equal("已选择 {0} 个项目", StringTable.Chinese["Status_Selected"]);
        Assert.Equal("{0} free", StringTable.English["Status_Free"]);
        Assert.Equal("{0} 可用", StringTable.Chinese["Status_Free"]);
        Assert.Equal("{0} free, {1} total", StringTable.English["Status_FreeOfTotal"]);
        Assert.Equal("{0} 可用，共 {1}", StringTable.Chinese["Status_FreeOfTotal"]);
        Assert.Contains("Status_ItemsCancelled", StringTable.Chinese.Keys);
        Assert.Contains("Status_ItemsFailed", StringTable.Chinese.Keys);
    }

    [Fact]
    public void Visible_status_producers_use_the_localization_table()
    {
        var pane = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Navigation", "PaneViewModel.cs"));
        var navigator = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.xaml.cs"));
        var folderStatus = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Views", "NavigatorPage.FolderStatus.cs"));

        Assert.Contains("StringTable.Format(\"Status_Items", pane, StringComparison.Ordinal);
        Assert.Contains("StringTable.Get(\"AccessDenied_Title\")", pane, StringComparison.Ordinal);
        Assert.Contains("DirectoryReadErrorKind.AccessDenied", pane, StringComparison.Ordinal);
        Assert.DoesNotContain("{count} items", pane, StringComparison.Ordinal);
        Assert.Contains("StringTable.Format(\"Status_Selected\"", navigator, StringComparison.Ordinal);
        Assert.Contains("DriveCapacity.FormatBytes", navigator, StringComparison.Ordinal);
        Assert.Contains("StringTable.Format(\"Status_FolderSize\"", folderStatus, StringComparison.Ordinal);
        Assert.Contains("StringTable.Get(\"Status_FolderCalculating\")", folderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("Layout_Details", navigator, StringComparison.Ordinal);
        Assert.DoesNotContain("{count} selected", navigator, StringComparison.Ordinal);
    }
}
