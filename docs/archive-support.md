# Archive support

FilesMate 1.1.88 uses an installed CompactMate for archive commands. If CompactMate is absent, ZIP commands use the built-in engine. Failure to start an installed CompactMate is reported; FilesMate does not repeat that operation through another engine.

| Operation | Built-in behavior |
| --- | --- |
| Compress to ZIP | Create a ZIP beside the selection. |
| New archive / file shelf compression | Choose the ZIP name and destination folder. |
| Extract here | Place archive contents in the current folder. |
| Extract to folder | Place each archive in a subfolder named after it. |
| Extract to another location | Choose the destination folder. |
| Smart extract | Keep a single top-level folder; otherwise create a folder named after the archive. |
| Existing destination | Use the normal merge, replace, skip and keep-both dialog. |

These commands are available through the file manager and search-result file actions. Ordinary double-click continues to use the Windows file association.

## Scope

Built-in support handles unencrypted ZIP entries using Store or Deflate, on local drives. Network locations, symbolic links, redirected folders and hard-linked input files are rejected. Other formats, encrypted ZIPs and more specialized archive features require a compatible archive app.

Each archive is limited to 100,000 entries/path nodes, 128 path segments, 64 MiB of central-directory/name metadata and 64 GiB of uncompressed data. File contents stream through a 64 KiB buffer; the complete payload is never loaded into memory. The index of entry names still takes memory proportional to the entry count. Metadata limits are checked before .NET allocates its entry collection.

## File safety and storage

Compression and extraction prepare their output in a temporary directory on the destination drive. The existing transfer pipeline then publishes it with conflict handling and undo. All selected archives finish preparation before publication begins. Corruption or cancellation during preparation leaves existing destination files unchanged. Cancellation during publication keeps completed work and its undo record; it does not roll back the entire batch.

Extraction needs free space for the uncompressed contents. Same-drive publication moves the prepared files, avoiding another full copy. Replacements follow the existing [undo backup budget](undo-backup-policy.md), including explicit confirmation when replacement cannot be undone. Cleanup runs after completion, failure and cancellation. A cleanup failure reports the remaining temporary path. Process termination or power loss can leave temporary contents; this version does not automatically delete such directories at startup.

The engine checks Windows filenames, traversal, duplicate names, file/directory conflicts, links, declared lengths and CRC values. Directory handles stabilize checked paths during preparation. Internet-zone information on downloaded ZIPs is propagated to extracted files; inability to preserve that metadata fails preparation.

The safety checks follow the concerns described in Microsoft's [.NET ZIP and TAR guidance](https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices). External CompactMate operations use that application's own implementation and limits.

## 中文说明

已安装 CompactMate 时优先调用它；未安装时使用内置 ZIP，支持进度、取消、目标位置选择和同名冲突处理。内置功能处理本地磁盘上的普通 ZIP，单包最多 100,000 项、解压内容最多 64 GiB。加密包、其他格式、网络位置和链接项目需要兼容的压缩软件。

解压内容先写入目标磁盘上的临时目录并校验，再进入现有文件转移流程。准备阶段失败或取消不会改动已有目标文件；开始写入目标之后取消，会保留已完成的结果及对应撤销记录。正常结束会清理临时文件，清理失败会显示剩余位置；断电或进程被强制结束后可能留下临时目录。

## 日本語

CompactMate がインストールされている場合は優先して使用し、未インストールの場合は標準の ZIP 機能を使用します。進捗表示、キャンセル、保存先の選択、同名ファイルの処理に対応しています。ローカルドライブ上の通常の ZIP を対象とし、1 つのアーカイブにつき最大 100,000 項目、展開後 64 GiB までです。暗号化アーカイブ、その他の形式、ネットワーク上の場所、リンク項目には対応する圧縮ソフトが必要です。

展開内容を保存先ドライブの一時フォルダーで検証してから配置します。準備中の失敗やキャンセルで既存の保存先ファイルを変更することはありません。配置開始後のキャンセルでは完了済みの結果と対応する履歴を保持します。通常終了時には一時ファイルを削除し、削除失敗時には残った場所を表示します。強制終了や停電後には一時フォルダーが残る場合があります。
