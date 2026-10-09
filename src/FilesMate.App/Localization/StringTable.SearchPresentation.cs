namespace FilesMate.App.Localization;

public static partial class StringTable
{
    private static IReadOnlyDictionary<string, (string English, string Chinese, string Japanese)> SearchPresentationEntries() =>
        new Dictionary<string, (string English, string Chinese, string Japanese)>(StringComparer.Ordinal)
        {
            ["Search_GlobalEntry"] = ("Global advanced search", "全局高级搜索", "全体の詳細検索"),
            ["Search_SelectFolderOrGlobal"] = ("Choose a folder or global search", "选择文件夹或全局搜索", "フォルダーを選ぶか全体検索を選択"),
            ["Search_SubmitGlobal"] = ("Search globally", "提交全局搜索", "全体を検索"),
            ["Search_SubmitFolder"] = ("Search this folder", "搜索当前文件夹", "このフォルダーを検索"),
        };
}
