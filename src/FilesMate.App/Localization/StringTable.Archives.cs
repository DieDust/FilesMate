namespace FilesMate.App.Localization;

public static partial class StringTable
{
    private static IReadOnlyDictionary<string, (string English, string Chinese, string Japanese)> ArchiveEntries() =>
        new Dictionary<string, (string English, string Chinese, string Japanese)>(StringComparer.Ordinal)
        {
            ["Archive_TitleCompress"] = ("Compress", "压缩", "圧縮"),
            ["Archive_TitleExtract"] = ("Extract", "解压", "展開"),
            ["Archive_CreateZip"] = ("Create ZIP", "创建 ZIP", "ZIP を作成"),
            ["Archive_Unsupported"] = ("This archive format needs CompactMate or another compatible archive app.", "此压缩格式需要使用 CompactMate 或其他兼容的压缩软件。", "この形式には CompactMate または対応する圧縮ソフトが必要です。"),
            ["Archive_Progress"] = ("{0} / {1} items · {2} / {3} MB", "{0} / {1} 项 · {2} / {3} MB", "{0} / {1} 件 · {2} / {3} MB"),
            ["Archive_Cancelled"] = ("Operation cancelled.", "操作已取消。", "操作をキャンセルしました。"),
            ["Archive_Completed"] = ("Completed: {0}", "已完成：{0}", "完了：{0}"),
            ["Archive_Cancel"] = ("Cancel", "取消", "キャンセル"),
            ["Archive_ChooseDestination"] = ("Choose a destination folder", "选择目标文件夹", "保存先フォルダーを選択"),
            ["Archive_DefaultName"] = ("Archive", "压缩文件", "アーカイブ"),
            ["Archive_ExternalOnly"] = ("Requires CompactMate", "需要 CompactMate", "CompactMate が必要"),
            ["Archive_CheckingProvider"] = ("Checking CompactMate…", "正在检查 CompactMate…", "CompactMate を確認中…"),
            ["Archive_Failed"] = ("Operation failed: {0}", "操作失败：{0}", "操作に失敗しました：{0}"),
            ["Archive_InvalidSelection"] = ("Select at least one existing file or folder.", "请至少选择一个已存在的文件或文件夹。", "既存のファイルまたはフォルダーを 1 つ以上選択してください。"),
            ["Archive_NameLabel"] = ("Archive name", "压缩文件名称", "アーカイブ名"),
            ["Archive_NameInvalid"] = ("Enter a valid ZIP filename without a folder path.", "请输入有效的 ZIP 文件名，不要包含文件夹路径。", "フォルダーのパスを含まない有効な ZIP ファイル名を入力してください。"),
            ["Archive_BuiltinHint"] = ("Built-in support handles ordinary ZIP archives. Encrypted ZIPs and other formats need CompactMate or another archive app.", "内置支持普通 ZIP。加密 ZIP 和其他格式请使用 CompactMate 或其他压缩软件。", "標準機能は通常の ZIP に対応しています。暗号化 ZIP やその他の形式には CompactMate などの圧縮ソフトをご利用ください。"),
            ["Archive_Cancelling"] = ("Cancelling…", "正在取消…", "キャンセル中…"),
            ["Archive_OutputLabel"] = ("Save to", "保存到", "保存先"),
            ["Archive_FolderPicker"] = ("Choose folder…", "选择文件夹…", "フォルダーを選択…"),
            ["Archive_ErrorInvalidPath"] = ("A file path is invalid or points outside the destination folder.", "文件路径无效，或指向目标文件夹以外的位置。", "ファイルのパスが無効か、保存先フォルダーの外を指しています。"),
            ["Archive_ErrorUnsafeLink"] = ("This operation includes a symbolic link or redirected folder. Choose regular files and folders.", "操作中包含符号链接或重定向文件夹，请选择普通文件和文件夹。", "シンボリックリンクまたはリダイレクトされたフォルダーが含まれています。通常のファイルやフォルダーを選択してください。"),
            ["Archive_ErrorDestinationNotEmpty"] = ("The destination folder is not empty. Choose an empty folder.", "目标文件夹不为空，请选择空文件夹。", "保存先フォルダーが空ではありません。空のフォルダーを選択してください。"),
            ["Archive_ErrorDestinationExists"] = ("The destination already exists. Choose another name or folder.", "目标已存在，请更换名称或文件夹。", "保存先は既に存在します。別の名前またはフォルダーを選択してください。"),
            ["Archive_ErrorInvalidArchive"] = ("The archive is damaged or its contents could not be verified.", "压缩文件已损坏，或内容校验失败。", "アーカイブが破損しているか、内容の検証に失敗しました。"),
            ["Archive_ErrorUnsupportedEntry"] = ("The archive contains encrypted data or an unsupported entry. Open it with a compatible archive app.", "压缩文件包含加密数据或不支持的内容，请用兼容的压缩软件打开。", "暗号化データまたは未対応の項目が含まれています。対応する圧縮ソフトで開いてください。"),
            ["Archive_ErrorTooManyEntries"] = ("This archive exceeds the built-in limit of 100,000 items.", "压缩文件超过内置功能的 100,000 项上限。", "このアーカイブは標準機能の上限である 100,000 項目を超えています。"),
            ["Archive_ErrorArchiveTooLarge"] = ("The expanded content exceeds the built-in limit of 64 GiB.", "解压后的内容超过内置功能的 64 GiB 上限。", "展開後の内容が標準機能の上限である 64 GiB を超えています。"),
            ["Archive_ErrorMetadataTooLarge"] = ("The archive's directory information exceeds the built-in limit of 64 MiB.", "压缩文件的目录信息超过内置功能的 64 MiB 上限。", "アーカイブのディレクトリ情報が標準機能の上限である 64 MiB を超えています。"),
            ["Archive_ErrorInsufficientSpace"] = ("There is not enough free space at the destination. Free some space or choose another drive.", "目标磁盘空间不足，请释放空间或选择其他磁盘。", "保存先の空き容量が不足しています。空き容量を増やすか、別のドライブを選択してください。"),
            ["Archive_ErrorSourceChanged"] = ("A source file changed during the operation. Wait for changes to finish and try again.", "来源文件在操作过程中发生了变化，请等待文件更改完成后重试。", "操作中に元のファイルが変更されました。変更が完了してから再試行してください。"),
            ["Archive_ErrorSecurityMetadataUnavailable"] = ("The file's internet-origin marker could not be read or preserved safely. Use a local drive that supports this marker, or extract with CompactMate.", "无法安全读取或保留文件的互联网来源标记。请使用支持此标记的本地磁盘，或改用 CompactMate 解压。", "ファイルのインターネット由来のマークを安全に読み取るか保持することができません。このマークに対応したローカルドライブを使用するか、CompactMate で展開してください。"),
            ["Archive_ErrorOperationFailed"] = ("The operation could not be completed. Check file permissions and available disk space, then try again.", "操作未能完成。请检查文件访问权限和磁盘可用空间，然后重试。", "操作を完了できませんでした。ファイルのアクセス権とディスクの空き容量を確認して再試行してください。"),
            ["Archive_TemporaryFilesRemain"] = ("Some temporary files could not be removed. Location: {0}", "部分临时文件未能清理，位置：{0}", "一部の一時ファイルを削除できませんでした。場所：{0}"),
        };
}
