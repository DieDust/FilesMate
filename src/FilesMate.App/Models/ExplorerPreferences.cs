namespace FilesMate.App.Models;

/// <summary>
/// Preferences that change file browsing behavior rather than window chrome.
/// </summary>
public sealed record ExplorerPreferences(
    bool ShowHiddenFiles,
    bool ConfirmPermanentDelete,
    bool OpenFoldersInNewTab,
    bool ShowFileExtensions,
    FolderViewKind DefaultView,
    DateFormatKind DateFormat,
    FolderStartupKind StartupFolder,
    double SidebarWidth,
    double PreviewWidth,
    double DetailsNameWidth,
    double DetailsModifiedWidth,
    double DetailsTypeWidth,
    double DetailsSizeWidth,
    bool DualPane,
    bool ShowFolderSizes,
    bool RestoreLastSession,
    bool OpenInExistingWindow,
    bool MixChineseAndLatin = false,
    bool ShowAlphabetNavigation = false,
    TabMemoryMode TabMemory = TabMemoryMode.Balanced,
    int AlphabetNavigationMinimumItemCount = 20,
    bool ShowAlphabetNavigationInDualPane = false)
{
    public const double SidebarWidthMin = 52;
    public const double SidebarWidthMax = 360;
    public const double SidebarCompactWidth = 120;
    public const double PreviewWidthMin = 200;
    public const double PreviewWidthMax = 640;
    public const int AlphabetNavigationMinimumItemCountMax = 10000;

    public static ExplorerPreferences Default { get; } = new(
        ShowHiddenFiles: false,
        ConfirmPermanentDelete: true,
        OpenFoldersInNewTab: false,
        ShowFileExtensions: true,
        DefaultView: FolderViewKind.Details,
        DateFormat: DateFormatKind.System,
        StartupFolder: FolderStartupKind.Desktop,
        SidebarWidth: 236,
        PreviewWidth: 320,
        DetailsNameWidth: 240,
        DetailsModifiedWidth: 148,
        DetailsTypeWidth: 100,
        DetailsSizeWidth: 88,
        DualPane: false,
        ShowFolderSizes: false,
        RestoreLastSession: true,
        OpenInExistingWindow: true);

    public static double ClampSidebarWidth(double width) =>
        Math.Clamp(width, SidebarWidthMin, SidebarWidthMax);

    public static double ClampPreviewWidth(double width) =>
        Math.Clamp(width, PreviewWidthMin, PreviewWidthMax);

    public static bool SidebarIsCompact(double width) => width < SidebarCompactWidth;

    public static ExplorerPreferences Sanitize(
        bool? showHiddenFiles,
        bool? confirmPermanentDelete,
        bool? openFoldersInNewTab,
        bool? showFileExtensions,
        string? defaultView,
        string? dateFormat,
        string? startupFolder,
        double? sidebarWidth = null,
        double? previewWidth = null,
        double? detailsNameWidth = null,
        double? detailsModifiedWidth = null,
        double? detailsTypeWidth = null,
        double? detailsSizeWidth = null,
        bool? dualPane = null,
        bool? showFolderSizes = null,
        bool? restoreLastSession = null,
        bool? openInExistingWindow = null,
        bool? mixChineseAndLatin = null,
        bool? showAlphabetNavigation = null,
        string? tabMemory = null,
        int? alphabetNavigationMinimumItemCount = null,
        bool? showAlphabetNavigationInDualPane = null) =>
        new(
            showHiddenFiles ?? Default.ShowHiddenFiles,
            confirmPermanentDelete ?? Default.ConfirmPermanentDelete,
            openFoldersInNewTab ?? Default.OpenFoldersInNewTab,
            showFileExtensions ?? Default.ShowFileExtensions,
            Parse(defaultView, Default.DefaultView),
            Parse(dateFormat, Default.DateFormat),
            Parse(startupFolder, Default.StartupFolder),
            ClampSidebarWidth(sidebarWidth ?? Default.SidebarWidth),
            ClampPreviewWidth(previewWidth ?? Default.PreviewWidth),
            ClampDetailsName(detailsNameWidth ?? Default.DetailsNameWidth),
            ClampDetailsMeta(detailsModifiedWidth ?? Default.DetailsModifiedWidth),
            ClampDetailsMeta(detailsTypeWidth ?? Default.DetailsTypeWidth),
            ClampDetailsMeta(detailsSizeWidth ?? Default.DetailsSizeWidth),
            dualPane ?? Default.DualPane,
            showFolderSizes ?? Default.ShowFolderSizes,
            restoreLastSession ?? Default.RestoreLastSession,
            openInExistingWindow ?? Default.OpenInExistingWindow,
            mixChineseAndLatin ?? false,
            showAlphabetNavigation ?? Default.ShowAlphabetNavigation,
            Parse(tabMemory, Default.TabMemory),
            ClampAlphabetNavigationMinimumItemCount(alphabetNavigationMinimumItemCount ?? Default.AlphabetNavigationMinimumItemCount),
            showAlphabetNavigationInDualPane ?? Default.ShowAlphabetNavigationInDualPane);

    public static double ClampDetailsName(double width) => Math.Clamp(width, 96, 560);

    public static double ClampDetailsMeta(double width) => Math.Clamp(width, 64, 480);

    public static int ClampAlphabetNavigationMinimumItemCount(int count) =>
        Math.Clamp(count, 0, AlphabetNavigationMinimumItemCountMax);

    private static TEnum Parse<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value.Trim(), out _))
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
    }
}

public enum FolderViewKind
{
    Details,
    Icons,
}

public enum DateFormatKind
{
    System,
    Iso,
}

public enum FolderStartupKind
{
    Desktop,
    Home,
}
