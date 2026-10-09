namespace FilesMate.App.Localization;

public static partial class StringTable
{
    private static IReadOnlyDictionary<string, (string English, string Chinese, string Japanese)> InlineFilterEntries() =>
        new Dictionary<string, (string English, string Chinese, string Japanese)>(StringComparer.Ordinal)
        {
            ["Column_RelativePath"] = ("Relative path", "相对路径", "相対パス"),
            ["Inline_Filter"] = ("Filter", "筛选", "絞り込み"),
            ["Inline_ClearFilters"] = ("Clear filters", "清除筛选", "条件をクリア"),
            ["Inline_ClearSearchAndFilters"] = ("Clear search and filters", "清除搜索和筛选", "検索と条件をクリア"),
            ["Inline_CurrentScope"] = ("Current folder", "当前文件夹", "現在のフォルダー"),
            ["Inline_RecursiveScope"] = ("Current folder and subfolders", "当前文件夹及子文件夹", "現在のフォルダーとサブフォルダー"),
            ["Inline_ChooseTypes"] = ("Choose types or extensions", "选择类型或扩展名", "種類・拡張子を選択"),
            ["Inline_Folders"] = ("Folders", "文件夹", "フォルダー"),
            ["Inline_Documents"] = ("Documents", "文档", "文書"),
            ["Inline_Images"] = ("Images", "图片", "画像"),
            ["Inline_Audio"] = ("Audio", "音频", "音声"),
            ["Inline_Video"] = ("Video", "视频", "動画"),
            ["Inline_Archives"] = ("Archives", "压缩包", "アーカイブ"),
            ["Inline_Applications"] = ("Applications and scripts", "程序与脚本", "アプリとスクリプト"),
            ["Inline_NoExtension"] = ("No extension", "无扩展名", "拡張子なし"),
            ["Inline_Extensions"] = ("Custom extensions", "自定义扩展名", "拡張子を指定"),
            ["Inline_Done"] = ("Done", "完成", "完了"),
            ["Inline_CustomSize"] = ("Custom size", "自定义大小", "サイズを指定"),
            ["Inline_BytesNote"] = ("KB/MB/GB use 1024 units. Inclusive bounds compare file bytes only.", "KB/MB/GB 沿用 1024 进制；上下限包含边界，仅比较文件字节。", "KB/MB/GB は1024単位。境界を含め、ファイルのバイト数のみ比較。"),
            ["Inline_ClearCondition"] = ("Clear this condition", "清除此条件", "この条件をクリア"),
            ["Inline_PreviousRetained"] = ("Previous valid conditions remain active.", "保留上次有效条件。", "前の有効な条件を維持します。"),
            ["Inline_InvalidExtensions"] = ("Invalid extension. Use jpg, .jpg or *.jpg; paths and arbitrary wildcards are not supported.", "扩展名格式无效。仅支持 jpg、.jpg 或 *.jpg，不支持路径或任意通配符。", "拡張子は jpg、.jpg、*.jpg 形式。パスや他のワイルドカードは使用できません。"),
            ["Inline_IncompleteDates"] = ("Complete both start and end dates.", "请完成开始和结束日期。", "開始日と終了日を指定してください。"),
            ["Inline_InvalidSize"] = ("Enter a valid nonnegative size range in ascending order.", "请填写有效且顺序正确的非负大小范围。", "有効な非負のサイズ範囲を昇順で入力してください。"),
            ["Inline_All"] = ("All", "全部", "すべて"),
            ["Inline_MultiSelect"] = ("Select multiple types. Matches use any selected type or extension.", "可多选，匹配任一所选类型或扩展名。", "複数選択できます。選択した種類または拡張子に一致します。"),
            ["Inline_ExtensionChoices"] = ("Extensions", "扩展名", "拡張子"),
            ["Inline_Add"] = ("Add", "添加", "追加"),
            ["Inline_TypeHint"] = ("jpg, .jpg or *.jpg; separate with semicolons.", "支持 jpg、.jpg 或 *.jpg，以分号分隔。", "jpg、.jpg、*.jpg をセミコロンで区切ります。"),
            ["Inline_DateHint"] = ("Includes both dates, in local time.", "按本地时间，包含起止日期。", "現地時間で開始日と終了日を含みます。"),
        };
}
