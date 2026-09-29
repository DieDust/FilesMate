using FilesMate.App.Localization;

namespace FilesMate.App.Models;

public sealed record SettingsSearchEntry(string Category, string TitleKey, string TargetName, string DescriptionKey = "", string Aliases = "")
{
    public string Title => StringTable.Get(TitleKey);
    public string CategoryTitle => StringTable.Get(Category switch
    {
        "appearance" => "SettingsAppearance", "files-folders" => "SettingsFilesAndFolders",
        "keyboard" => "SettingsKeyboard", "search" => "SettingsSearch", "tags" => "SettingsTags",
        "advanced" => "SettingsAdvanced", "about" => "SettingsAbout", _ => "SettingsGeneral"
    });
}

public static class SettingsSearchCatalog
{
    // Index metadata, not live controls: searching never loads a page or changes settings.
    public static IReadOnlyList<SettingsSearchEntry> Entries { get; } =
    [
        new("general", "Language_Title", "LanguageBox", "Language_Description", ""),
        new("general", "StartupFolderTitle", "StartupCard", "StartupFolderDescription", ""),
        new("general", "RestoreLastSessionTitle", "RestoreSessionCard", "RestoreLastSessionDescription", ""),
        new("general", "OpenInExistingWindowTitle", "OpenInExistingWindowCard", "OpenInExistingWindowDescription", ""),
        new("general", "OpenFoldersNewTabTitle", "OpenFoldersCard", "OpenFoldersNewTabDescription", ""),
        new("general", "Tab_MemoryMode", "TabMemoryBox", "Tab_MemoryHint", ""),
        new("appearance", "UseBundledFileIcons", "FileIconsCard", "UseBundledFileIconsHint", ""),
        new("appearance", "ThemeSection", "ThemeCard", "ThemeDescription", ""),
        new("appearance", "Font_Family", "FileFontCard", "Font_Hint", "字体 字体大小 清晰 font typography"),
        new("appearance", "Font_NameSize", "FileNameSizeBox", "Font_Hint", "字号 文件夹 文件 font size"),
        new("appearance", "Font_DetailsSize", "FileDetailsSizeBox", "Font_Hint", "字号 日期 大小 font size"),
        new("appearance", "Backdrop", "BackdropCard", "BackdropDescription", ""),
        new("appearance", "StatusBar", "StatusBarCard", "StatusBarDescription", ""),
        new("appearance", "Toolbar", "ToolbarCard", "ToolbarDescription", ""),
        new("appearance", "GlassEffects", "GlassEffectCard", "GlassEffectsDescription", ""),
        new("appearance", "Transparency", "TransparencyCard", "", ""),
        new("appearance", "ReduceMotion", "ReduceMotionCard", "ReduceMotionDescription", ""),
        new("files-folders", "Archive_Preferred", "ArchiveProviderBox", "Archive_PreferredHint", ""),
        new("files-folders", "Opening_FileTitle", "FileOpeningCard", "Opening_Hint", "single double click 单击 双击 名称 标题 打开"),
        new("files-folders", "Opening_FolderTitle", "FolderOpeningCard", "Opening_FolderHint", "single double click 单击 双击 名称 标题 打开"),
        new("files-folders", "Favorites_Bar", "FavoritesToggle", "Favorites_SettingsHint", ""),
        new("files-folders", "Setup_Guide", "FeatureSetupButton", "Setup_GuideHint", ""),
        new("files-folders", "View_Scope", "ViewScopeBox", "View_ScopeHint", ""),
        new("files-folders", "DefaultViewTitle", "StartupViewCard", "DefaultViewDescription", ""),
        new("files-folders", "ShowExtensionsTitle", "ExtensionsCard", "ShowExtensionsDescription", ""),
        new("files-folders", "ShowHiddenFilesTitle", "HiddenFilesCard", "ShowHiddenFilesDescription", ""),
        new("files-folders", "ShowFolderSizesTitle", "FolderSizesCard", "ShowFolderSizesDescription", ""),
        new("files-folders", "Sort_MixedPinyin", "MixedNameSortToggle", "Sort_MixedPinyinHint", ""),
        new("files-folders", "View_AlternatingRows", "AlternatingRowsCard", "View_AlternatingRowsHint", "斑马纹 隔行 底色 条纹 zebra stripes alternating rows"),
        new("files-folders", "Sort_DefaultDirection", "DefaultSortDirectionCard", "Sort_DefaultDirectionHint", "正序 倒序 升序 降序 修改日期 大小 ascending descending sort order"),
        new("files-folders", "Grouping_Default", "DefaultGroupingBox", "Grouping_DefaultHint", "文件夹置顶 文件置顶 混排 排列 grouping folders first mixed files"),
        new("files-folders", "DateFormatTitle", "DateFormatCard", "DateFormatDescription", ""),
        new("files-folders", "AlphabetNavigationTitle", "AlphabetCard", "AlphabetNavigationDescription", ""),
        new("files-folders", "AlphabetMinimumItemsTitle", "AlphabetMinimumItemsCard", "AlphabetMinimumItemsDescription", ""),
        new("files-folders", "AlphabetDualPaneTitle", "AlphabetDualPaneCard", "AlphabetDualPaneDescription", ""),
        new("files-folders", "ConfirmDeleteSettingTitle", "ConfirmDeleteCard", "ConfirmDeleteSettingDescription", ""),
        new("files-folders", "Backup_Title", "BackupManageButton", "Backup_Policy", ""),
        new("files-folders", "SidebarPinsTitle", "SidebarPinsCard", "SidebarPinsDescription", ""),
        new("search", "SearchIndexTitle", "IndexCard", "SearchIndexDescription", ""),
        new("search", "SearchProgress", "ProgressCard", "", ""),
        new("search", "SearchDepth", "DepthCard", "SearchDepthDescription", ""),
        new("search", "SearchLocation", "IndexLocationCard", "", ""),
        new("search", "SearchParallel", "ParallelCard", "SearchParallelDescription", ""),
        new("search", "SearchAutoRefresh", "AutoRefreshCard", "SearchAutoRefreshDescription", ""),
        new("advanced", "IconCacheTitle", "IconCacheCard", "IconCacheDescription", ""),
        new("advanced", "DataFolderTitle", "DataFolderCard", "DataFolderDescription", ""),
        new("advanced", "DefaultAppTitle", "DefaultAppCard", "DefaultAppDescription", ""),
        new("advanced", "ClassicExplorerTitle", "ClassicExplorerCard", "ClassicExplorerDescription", ""),
        new("keyboard", "SettingsKeyboard", "ShortcutRows", "", "快捷键 shortcuts hotkey keyboard"),
        new("tags", "SettingsTags", "NewTagButton", "", "标签 tag 分类 rename"),
        new("about", "SettingsAbout", "Heading", "", "版本 version update 更新"),
        new("search", "Search_Residency", "GlobalSearchResident", "Search_ResidencyHint", "后台 常驻 resident"),
        new("search", "Search_StartAtLogin", "GlobalSearchStartup", "Search_StartupHint", "开机 startup"),
        new("search", "Search_PreviewFiles", "GlobalSearchPreview", "Search_PreviewHint", "预览 thumbnail"),
        new("search", "SearchShortcut", "GlobalSearchHotkey", "", "Alt Space 空格 热键"),
        new("search", "Search_TrayAction", "GlobalSearchTrayAction", "", "托盘 tray"),
        new("search", "SearchDrives", "DrivesHeader", "", "搜索范围 目录 文件夹 index"),
        new("search", "SearchExclusions", "ExclusionsBox", "", "排除 忽略 exclude"),
    ];

    public static IReadOnlyList<SettingsSearchEntry> Search(string? query)
    {
        var tokens = (query ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return [];
        return Entries.Where(entry => tokens.All(token => Text(entry).Contains(token, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => tokens.Count(token => entry.Title.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(entry => entry.Category, StringComparer.Ordinal).Take(12).ToArray();
    }

    private static string Text(SettingsSearchEntry entry) => string.Join(" ",
        entry.Title, entry.CategoryTitle, Translations(entry.TitleKey), Translations(entry.DescriptionKey), entry.Aliases);

    private static string Translations(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (StringTable.Additional.TryGetValue(key, out var extra)) return $"{extra.English} {extra.Chinese} {extra.Japanese}";
        return string.Join(" ", StringTable.Get(key), StringTable.English.GetValueOrDefault(key),
            StringTable.Chinese.GetValueOrDefault(key), StringTable.Japanese.GetValueOrDefault(key));
    }
}
