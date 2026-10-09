using System.Globalization;

namespace FilesMate.App.Localization;

public static partial class StringTable
{
    private static readonly IReadOnlyDictionary<string, (string English, string Chinese, string Japanese)> PortableText =
        new Dictionary<string, (string English, string Chinese, string Japanese)>(StringComparer.Ordinal)
        {
            ["Portable_UpdateAction"] = ("Update manually", "手动更新", "手動更新"),
            ["Portable_ManualUpdate"] = ("Download the new portable ZIP from GitHub Releases. Close every FilesMate window, replace the program files, and keep the data folder. This portable copy cannot run the installer updater.", "请从 GitHub Releases 下载新版免安装 ZIP。关闭全部 FilesMate 窗口后替换程序文件，保留 data 文件夹。免安装版不会运行安装器更新。", "GitHub Releases から新しいポータブル ZIP を取得してください。すべての FilesMate ウィンドウを閉じ、data フォルダーを残してプログラムを置き換えます。ポータブル版ではインストーラーを実行しません。"),
            ["Portable_OpenDownloads"] = ("Open downloads", "打开下载页面", "ダウンロードを開く"),
            ["Portable_NoIntegration"] = ("Portable mode keeps its data beside the application and does not register Windows integration or login startup.", "免安装模式的数据保存在程序旁，不注册 Windows 文件关联或开机启动。", "ポータブル版はアプリの隣にデータを保存し、Windows の関連付けやログイン時の起動を登録しません。"),
        };

    private static bool TryPortableText(string key, CultureInfo culture, out string value)
    {
        if (!PortableText.TryGetValue(key, out var entry)) { value = ""; return false; }
        value = culture.TwoLetterISOLanguageName switch { "zh" => entry.Chinese, "ja" => entry.Japanese, _ => entry.English };
        return true;
    }
}
