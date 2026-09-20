# Localization

FilesMate supports English (`en-US`), Simplified Chinese (`zh-CN`) and Japanese (`ja-JP`). Choose **Settings → General → Display language**. The default follows the Windows display language; unsupported languages fall back to English. Selecting a different language saves the choice and automatically restarts the file manager and any running global search process. Active file transfers finish first. A one-use session handoff restores open tabs and selection, even when normal session restoration is off; the preference itself stays unchanged. Date and number formats continue to follow the user's regional settings.

## Resource ownership

The shared source of truth is `src/FilesMate.App/Localization/`:

- `StringTable.cs`: existing English and Simplified Chinese resources and lookup logic.
- `StringTable.Japanese.cs`: Japanese translations of the existing keys.
- `StringTable.Additional.cs`: newer resources, with `(English, Chinese, Japanese)` values beside each stable key.
- `LanguageSettings.cs`: shared preference persistence and startup selection. The search host links these same files rather than maintaining a second translation table.

Use `StringTable.Get(key)` for labels and `StringTable.Format(key, arguments)` for complete messages. WinUI and WPF each provide a `LocalizedText` markup extension. Use `StringTable.Html` or HTML-encode formatted text when writing a preview HTML fragment. Do not assemble grammatical sentences by joining separately translated fragments.

The `Strings/*/Resources.resw` files are generated mirrors for Windows resource tooling. After editing translations, run:

```powershell
pwsh ./scripts/sync-localization-resources.ps1
dotnet test ./tests/FilesMate.App.Tests/FilesMate.App.Tests.csproj -c Release --filter FullyQualifiedName~Localization
```

Commit the source tables and regenerated resources together. Coverage tests check matching keys, nonempty values, format placeholders and resource synchronization. Keep keys stable; never translate persistence keys, enum values, command identifiers, executable names or file formats.

## Writing translations

Use short action labels and complete, helpful explanations. Preserve `{0}`, `{1}`, formatting specifiers and keyboard shortcuts. Japanese should use familiar Windows terminology, without adding spaces between Japanese words. Keep FilesMate, OneDrive, WPS and Microsoft Office as product names. Use these terms consistently:

| English | 简体中文 | 日本語 |
|---|---|---|
| Settings | 设置 | 設定 |
| Appearance | 外观 | 外観 |
| File shelf | 文件暂存架 | ファイルシェルフ |
| Favorites bar | 收藏栏 | お気に入りバー |
| Global search | 全局搜索 | グローバル検索 |
| Alphabet navigation | 字母定位 | アルファベットナビゲーション |
| Layered | 分层 | 階層型 |
| Unified | 一体化 | 一体型 |
| Rename | 重命名 | 名前の変更 |

File names, drive labels, custom tags, saved bookmark names and document contents belong to the user and retain their original text. Windows dialogs, file type descriptions and third-party preview handlers can use their own language. Technical exception details from the operating system can also retain the system language.

## Release notes

Release notes are part of the signed update payload, not machine-translated on the client. Maintain a UTF-8 JSON map in `release-notes/<version>.json` with `zh-CN`, `en-US`, and `ja-JP` entries. From 1.1.70 onward, `scripts/sign-update.ps1` requires all three entries:

```powershell
pwsh ./scripts/sign-update.ps1 -Installer ./artifacts/releases/FilesMate-Setup-1.1.70-preview.20260919-win-x64.exe -Version 1.1.70.0 -DisplayVersion 1.1.70-preview.20260919 -LocalizedNotesPath ./release-notes/1.1.70.json
```

The client selects an exact display-language match, then a parent language, then the supported language-family translation, then English, and finally the legacy `notes` field. Dates and regional number preferences do not change the note language. The envelope stays at schema 1; older clients ignore `localizedNotes` and display the legacy text. If `-Notes` is omitted, the signing script supplies English for older clients. New clients still accept previously published manifests containing only `notes`.

Each translation is limited to 8,000 characters, the map to 16 languages, and the entire signed envelope to 64 KB. Translations are covered by the same RSA-PSS signature as the installer hash; do not edit a published payload without signing it again.

## UI checks

Check both light and dark themes, narrow and wide windows, and Windows scaling. English labels can be much longer than Chinese; Japanese needs enough line height. Prefer wrapping descriptions or stacking setting controls below them to clipping. Keep action buttons reachable by keyboard. Check menus, tooltips, empty states, validation messages, onboarding, update prompts and the separate search window, not only the main settings page.

For isolated WinUI testing, build with `-p:FilesMateUITest=true` into an ignored artifact directory. Put `{"Language":"en-US"}` or `{"Language":"ja-JP"}` in that build's `test-profile/language.json`. Setting `FILESMATE_LOCALIZATION_SMOKE=1` runs the settings layout checks and writes `localization-<culture>.json` beside the test executable. This hook is excluded from production builds. Never use a personal profile for automated UI tests.

Translation coverage is not a substitute for native-speaker review. Improvements to terminology and natural phrasing are welcome alongside screenshots of the affected interface.
