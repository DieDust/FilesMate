# Archive support

FilesMate 1.1.119-preview.9 includes a bundled archive engine and a configurable choice of external archive app. Automatic selection tries CompactMate, Bandizip, 7-Zip, and WinRAR for commands they support, then uses the built-in engine. You can select the built-in engine explicitly in Settings. Failure to start a selected external app is reported; an operation that has already been launched is not silently repeated through another engine.

| Operation | Built-in behavior |
| --- | --- |
| Compress to ZIP | Create a ZIP beside the selection. |
| Compress to 7z | Create a 7z beside the selection. |
| New archive / file shelf compression | Choose the ZIP name and destination folder. |
| Extract here | Place archive contents in the current folder. |
| Extract to folder | Place each archive in a subfolder named after it. |
| Extract to another location | Choose the destination folder. |
| Smart extract | Keep a single top-level folder; otherwise create a folder named after the archive. |
| Existing destination | Use the normal merge, replace, skip and keep-both dialog. |

These commands are available through the file manager and search-result file actions. Ordinary double-click continues to use the Windows file association.

## Scope

Built-in creation supports ZIP and 7z on local drives. Extraction uses the bundled SharpCompress engine. Regression fixtures cover encrypted ZIP and 7z (with password prompts), solid RAR, renamed RAR5 volume sets, legacy split ZIP, numeric split 7z, and TAR.GZ. Support depends on the archive's compression and encryption methods; this is not a claim that every variant is supported. Creating password-protected archives is not exposed by the built-in creation commands.

The volume resolver groups supported split sets without renaming the source files and rejects missing or ambiguous parts. Network locations, symbolic links, redirected folders and hard-linked input files remain outside the built-in local-file safety boundary. External archive apps may support additional formats and locations.

The built-in path and output checks bound entries/path nodes to 100,000, paths to 128 segments, and extracted contents to 64 GiB. ZIP preflight additionally checks central-directory metadata before the archive reader allocates its entry collection. Archive metadata and decompression dictionaries still consume memory according to the format and entry count.

## File safety and storage

Compression and extraction prepare their output in a temporary directory on the destination drive. The existing transfer pipeline then publishes it with conflict handling and undo. All selected archives finish preparation before publication begins. Corruption or cancellation during preparation leaves existing destination files unchanged. Cancellation during publication keeps completed work and its undo record; it does not roll back the entire batch.

Extraction needs free space for the uncompressed contents. Same-drive publication moves the prepared files, avoiding another full copy. Replacements follow the existing [undo backup budget](undo-backup-policy.md), including explicit confirmation when replacement cannot be undone. Cleanup runs after completion, failure and cancellation. A cleanup failure reports the remaining temporary path. Process termination or power loss can leave temporary contents; this version does not automatically delete such directories at startup.

The engine checks Windows filenames, traversal, duplicate names, file/directory conflicts, links, declared lengths and CRC values. Directory handles stabilize checked paths during preparation. Internet-zone information on downloaded ZIPs is propagated to extracted files; inability to preserve that metadata fails preparation.

The safety checks follow the concerns described in Microsoft's [.NET ZIP and TAR guidance](https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices). External archive operations use the selected application's own implementation and limits.

## 中文说明

可在设置中选择内置引擎或外部压缩软件。自动模式依次尝试 CompactMate、Bandizip、7-Zip、WinRAR 支持的操作，再使用内置引擎。内置功能可创建 ZIP、7z，并解压支持的加密包和分卷包，需要时提示密码。回归样本覆盖加密 ZIP/7z、固实 RAR、RAR5 分卷、ZIP/7z 分卷和 TAR.GZ；不代表支持所有压缩或加密变体。内置创建功能暂不提供加密选项。单包最多 100,000 项、解压内容最多 64 GiB；网络位置和链接项目仍需兼容的外部软件。

解压内容先写入目标磁盘上的临时目录并校验，再进入现有文件转移流程。准备阶段失败或取消不会改动已有目标文件；开始写入目标之后取消，会保留已完成的结果及对应撤销记录。正常结束会清理临时文件，清理失败会显示剩余位置；断电或进程被强制结束后可能留下临时目录。

## 日本語

設定で内蔵エンジンまたは外部の圧縮ソフトを選べます。自動選択は CompactMate、Bandizip、7-Zip、WinRAR の対応する操作を順に試し、内蔵エンジンに切り替えます。内蔵機能は ZIP・7z の作成と、対応する暗号化・分割アーカイブの展開を行い、必要に応じてパスワードを求めます。回帰テストでは暗号化 ZIP/7z、ソリッド RAR、RAR5・ZIP・7z の分割ファイル、TAR.GZ を確認していますが、すべての方式への対応を保証するものではありません。内蔵の作成機能には暗号化の選択肢がありません。最大 100,000 項目、展開後 64 GiB までで、ネットワーク上の場所やリンク項目には対応する外部ソフトが必要です。

展開内容を保存先ドライブの一時フォルダーで検証してから配置します。準備中の失敗やキャンセルで既存の保存先ファイルを変更することはありません。配置開始後のキャンセルでは完了済みの結果と対応する履歴を保持します。通常終了時には一時ファイルを削除し、削除失敗時には残った場所を表示します。強制終了や停電後には一時フォルダーが残る場合があります。
