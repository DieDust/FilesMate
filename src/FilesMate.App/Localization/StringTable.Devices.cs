namespace FilesMate.App.Localization;

public static partial class StringTable
{
    private static IReadOnlyDictionary<string, (string English, string Chinese, string Japanese)> DeviceEntries() =>
        new Dictionary<string, (string English, string Chinese, string Japanese)>(StringComparer.Ordinal)
        {
            ["Device_PreviewHint"] = ("A preview is not available for this device item. Double-click it to open it with Windows.", "此设备项目暂时没有可用的预览，可双击通过 Windows 打开。", "このデバイス項目のプレビューはありません。ダブルクリックすると Windows で開きます。"),
            ["Device_CopyTo"] = ("Copy to…", "复制到…", "コピー先…"),
            ["Device_WritableHint"] = ("Copy files here with Paste or drag and drop. Keep the device connected until the transfer finishes.", "可粘贴或拖入文件。传输完成前请保持设备连接。", "貼り付けやドラッグでファイルをコピーできます。転送完了まで接続を維持してください。"),
            ["Device_ReadOnlyHint"] = ("This device folder allows copying files out, but does not accept files. Copy the selected items and paste them into a folder on your computer.", "此设备目录支持复制文件到电脑，不接受写入。选中文件并复制，再到电脑中的目标文件夹粘贴。", "このフォルダーからパソコンへコピーできますが、書き込みは許可されていません。項目をコピーし、パソコン内のフォルダーに貼り付けてください。"),
            ["Device_ClipboardReady"] = ("Ready to copy. Open a destination folder and paste.", "已复制，请打开目标文件夹后粘贴。", "コピーしました。コピー先のフォルダーを開いて貼り付けてください。"),
            ["Device_Copying"] = ("Copying files…", "正在复制文件…", "ファイルをコピー中…"),
            ["Device_CopyCompleted"] = ("Copied {0} items.", "已复制 {0} 个项目。", "{0} 個の項目をコピーしました。"),
            ["Device_CopyCancelled"] = ("Copy cancelled. Completed: {0}; incomplete or skipped: {1}. Source files were kept.", "复制已取消：已完成 {0} 个项目，未完成或跳过 {1} 个。源文件已保留。", "コピーをキャンセルしました。完了：{0} 件、未完了またはスキップ：{1} 件。元のファイルは保持されています。"),
            ["Device_CopyIncomplete"] = ("Completed: {0}; incomplete or skipped: {1}. Check the device connection and destination space. Source files were kept.", "已完成 {0} 个项目，未完成或跳过 {1} 个。请检查设备连接和目标可用空间。源文件已保留。", "完了：{0} 件、未完了またはスキップ：{1} 件。接続と空き容量を確認してください。元のファイルは保持されています。"),
            ["Device_TransferFailed"] = ("Could not copy the files. Check the connection, access permission and available space, then try again.", "无法复制文件，请检查设备连接、访问权限和可用空间后重试。", "コピーできませんでした。接続、アクセス権、空き容量を確認して再試行してください。"),
            ["Device_InvalidClipboard"] = ("These items cannot be pasted here. Copy the device files from a FilesMate device tab and try again.", "无法粘贴这些项目。请在 FilesMate 的设备标签页复制文件后重试。", "貼り付けできません。FilesMate のデバイスタブからファイルをコピーして再試行してください。"),
            ["Device_CopyOnly"] = ("Use Copy for device transfers. Cut and move are not available here.", "设备传输请使用“复制”。此处暂不支持剪切和移动。", "デバイスとの転送には「コピー」を使用してください。切り取りと移動には対応していません。"),
            ["Device_BrowseHint"] = ("Browse files shared by this device. Unlock it and allow access if prompted.", "浏览设备向 Windows 开放的文件。如有提示，请解锁设备并允许访问。", "デバイスが Windows に公開しているファイルを表示します。必要に応じてロックを解除し、アクセスを許可してください。"),
            ["Device_Loading"] = ("Reading device…", "正在读取设备…", "デバイスを読み込み中…"),
            ["Device_Empty"] = ("No items are available. If files are missing, unlock the device and allow this computer to access it, then refresh.", "暂无可显示的项目。如果缺少文件，请解锁设备并允许此电脑访问，然后刷新。", "表示できる項目がありません。ファイルが見つからない場合は、ロックを解除してこのコンピューターのアクセスを許可し、更新してください。"),
            ["Device_Unavailable"] = ("The device is disconnected, locked, or not responding. Reconnect or unlock it, then refresh.", "设备已断开、被锁定或暂时无响应。请重新连接或解锁后刷新。", "デバイスが切断、ロックされているか、応答していません。再接続またはロック解除後に更新してください。"),
            ["Device_OpenFailed"] = ("Could not open this device file. Check the connection and try again.", "无法打开设备中的文件，请检查连接后重试。", "デバイス内のファイルを開けません。接続を確認して再試行してください。"),
            ["Device_EjectBusy"] = ("FilesMate has unfinished file operations. Check any copy, move, archive or conflict windows. Finish or cancel those operations, then try ejecting again. Keep the device connected.", "FilesMate 还有文件操作未结束。请检查复制、移动、压缩解压或同名冲突窗口，完成或取消操作后，再次尝试弹出。暂时保持设备连接。", "FilesMate のファイル操作が完了していません。コピー、移動、圧縮・展開、名前の競合のウィンドウを確認し、操作を完了またはキャンセルしてから再試行してください。接続は維持してください。"),
            ["Device_EjectPreparing"] = ("Preparing to eject. Releasing open device folders and previews…", "正在准备弹出，释放已打开的设备文件夹和预览…", "取り外しを準備しています。デバイスのフォルダーとプレビューを解放中…"),
            ["Device_EjectRemoving"] = ("Waiting for Windows to safely eject the device. Keep it connected until the result appears.", "正在等待 Windows 安全弹出设备。结果显示前请保持连接。", "Windows による安全な取り外しを待っています。結果が表示されるまで接続を維持してください。"),
            ["Device_EjectDebugging"] = ("The phone's USB debugging connection (ADB) is still in use. Close the debugging or mirroring app, or turn off USB debugging in the phone's Developer options, then try ejecting again. Turning it off disconnects debugging sessions. Keep the cable connected until removal succeeds.", "手机的 USB 调试连接（ADB）仍被占用。请退出正在连接手机的调试或投屏软件，或在手机“开发者选项”中关闭“USB 调试”，然后再次弹出。关闭后会断开调试连接；弹出成功前请保持数据线连接。", "スマートフォンの USB デバッグ接続（ADB）が使用中です。接続中のデバッグ・画面ミラーリングアプリを終了するか、スマートフォンの開発者向けオプションで USB デバッグをオフにして再試行してください。オフにするとデバッグ接続が切断されます。取り外しが成功するまでケーブルを接続したままにしてください。"),
            ["Device_EjectBlockingInterface"] = ("Device connection in use: {0}", "被占用的设备接口：{0}", "使用中のデバイス接続：{0}"),
            ["Device_CanPaste"] = ("Device · Copy and paste", "设备 · 可复制和粘贴", "デバイス · コピー・貼り付け可能"),
            ["Device_ReadOnly"] = ("Device · Read only", "设备 · 只读", "デバイス · 読み取り専用"),
            ["Device_SearchCurrentFolder"] = ("Current device folder: {0} matches", "当前设备文件夹：{0} 个结果", "現在のデバイスフォルダー：{0} 件"),
            ["Device_EjectUnavailable"] = ("This device is disconnected or does not support safe removal here.", "设备已断开，或不支持在此安全弹出。", "デバイスが切断されているか、ここでの安全な取り外しに対応していません。"),
            ["Device_EjectSuccess"] = ("The device was safely ejected. You can unplug it now.", "设备已安全弹出，现在可以拔出。", "デバイスを安全に取り外しました。接続を解除できます。"),
            ["Device_EjectDenied"] = ("Windows could not eject this device. Close files and programs using it, then try again. Keep the device connected.", "Windows 未能弹出设备。请关闭正在使用它的文件和程序后重试，暂时保持设备连接。", "Windows が取り外しを拒否しました。使用中のファイルやアプリを閉じて再試行してください。接続は維持してください。"),
            ["Device_EjectShowBlockers"] = ("Show file users", "查看文件占用", "ファイルの使用元を表示"),
            ["Device_EjectRetry"] = ("Retry safe eject", "重试安全弹出", "安全な取り外しを再試行"),
            ["Device_EjectReleaseOwn"] = ("Release FilesMate previews", "释放 FilesMate 的预览占用", "FilesMate のプレビューを解放"),
            ["Device_EjectNoFileLocks"] = ("No open file handles were found on this drive. A driver, system service or device interface may still be using it. Keep it connected and retry after closing related apps.", "未发现占用此磁盘文件的程序。驱动、系统服务或设备接口仍可能在使用它；请保持连接，关闭相关程序后重试。", "このドライブで開かれているファイルは見つかりませんでした。ドライバー、システムサービス、デバイス接続が使用中の可能性があります。接続を維持し、関連アプリを閉じてから再試行してください。"),
        };
}
