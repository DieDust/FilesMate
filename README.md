<div align="center">
  <img src="src/FilesMate.App/Assets/Branding/FilesMate.svg" width="88" alt="FilesMate logo" />
  <h1>FilesMate</h1>
  <p>A native Windows file manager. Browse, find, preview and organize — in one place.</p>
  <p><b>English</b> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a></p>
  <p><a href="https://github.com/DieDust/FilesMate/releases">Download</a> · <a href="#features">Features</a> · <a href="#community">Community</a> · <a href="CONTRIBUTING.md">Contribute</a></p>
  <p><img src="https://img.shields.io/badge/Windows_11-x64-0078D4" alt="Windows 11 x64" /> <img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="Apache 2.0" /> <img src="https://img.shields.io/badge/status-preview-orange" alt="Preview software" /></p>
</div>

![FilesMate dark workspace with tabs, breadcrumb navigation and file details](docs/images/workspace-dark.png)

FilesMate combines a familiar file browser with tools for the work around your files: compare two folders, collect files from different locations, rename a batch with a preview, or summon search without keeping the main window open.

## Download and get started

1. Download the **win-x64 installer** from [GitHub Releases](https://github.com/DieDust/FilesMate/releases).
2. Install on **Windows 11 22H2 (build 22621) or later, x64**. The installer includes the .NET runtime. This build does not support Windows 10 or ARM64.
3. Choose optional features in the first-run guide. Configure indexing and the background search shortcut in **Settings → Search**.

Already installed? Open **Settings → About → Check for updates**. Updates use the project's HTTPS server with a signed manifest and installer hash verification. This is preview software; see [known limitations](docs/public-release.md).

## Features

### Tabs and two panes: less back-and-forth

Keep folders open in tabs, or turn on **Dual pane** to work with two folders side by side. Copy or move a selection to the other pane, reopen a closed tab with `Ctrl+Shift+T`, and restore your tabs at startup. Idle-tab hibernation is configurable.

![Two folders open side by side](docs/images/dual-pane.png)

### Global search that stays available

With background search enabled, press **Alt+Space** by default to search indexed filenames and applications. Filter by type, preview supported files, or jump to their folder. The search process can stay open when you close the file manager; its shortcut, residency and login startup are configurable.

This is **filename search**, not document-content search. You choose the index scope; building and refreshing the index is handled by the file manager.

![Global search showing matching project files](docs/images/global-search.png)

### Preview before opening

Use **Alt+P** for the side preview, or **Space** for quick preview. Inspect text and code, images, PDF, Markdown and supported Office formats without repeatedly opening another application. Format support and conversion behavior depend on the file and installed Windows components.

![JSON preview beside the file list](docs/images/document-preview.png)

### File shelf: collect first, organize next

Gather files from different folders into the **file shelf**, then copy or move the selected items to a destination. The shelf stores references to the originals; collecting them does not create duplicate file copies. Removing a shelf reference does not delete the original.

![File shelf holding references from several folders](docs/images/file-shelf.png)

### Batch rename with a before-and-after view

Select several items and press **F2**. Choose a naming rule, inspect original and proposed names in a compact table, and check reported issues before applying. Extensions are preserved by default. Supported rename operations can be undone with **Ctrl+Z**.

![Batch rename with find-and-replace and name previews](docs/images/batch-rename.png)

### Make the workspace yours

Choose light, dark or system theme; adjust glass transparency, accent color and layered or unified surfaces. Use FilesMate icons or Windows file-association icons. Customize home sections, bookmarks, tags and folder views. **English, Simplified Chinese and Japanese** are included; changing the language automatically restarts the app after transfers finish and restores tabs.

![FilesMate light theme](docs/images/workspace-light.png)

<details>
<summary>More everyday tools</summary>

| Tool | What it does |
| --- | --- |
| Bookmarks and tags | Keep frequently used locations and tagged files within reach. |
| Folder views | Details/icon views, sortable and resizable columns, per-folder or global preferences. |
| Optional letter navigation | Jump by initial in larger folders; off by default, with item-count and dual-pane conditions. |
| Chinese filename sorting | Optional offline pinyin-based ordering for mixed Chinese/English names. |
| Conflict handling | Replace eligible files, skip, or keep both with a numbered name; merge folders and resolve nested conflicts. |
| Delete feedback | Distinguish recycling from permanent deletion. Windows asks before deleting a file that cannot fit in the Recycle Bin. |
| Undo backups | Review and manage retained replacement backups in Settings. Permanent deletion is not undoable. |
| Drag selection | Select across scrolling content, including mouse-wheel scrolling during a drag. |

</details>

## Keyboard essentials

| Shortcut | Action |
| --- | --- |
| `Alt+Space` | Global search, when enabled; configurable |
| `Ctrl+L` | Edit the current path |
| `F2` | Rename or batch rename |
| `Space` / `Alt+P` | Quick preview / side preview |
| `Ctrl+Shift+T` | Reopen a closed tab |
| `Ctrl+Shift+P` | Search commands |
| `Ctrl+Z` | Undo a supported operation |

## Community

**FilesMate QQ group: `984027951`**. Scan in QQ to join, share ideas and discuss everyday usage. The group is primarily Chinese-speaking; English bug reports and contributions are welcome on GitHub.

<img src="docs/images/community-qq.jpg" width="280" alt="FilesMate QQ community QR code, group 984027951" />

For reproducible bugs, [open an issue](https://github.com/DieDust/FilesMate/issues) with the app version, Windows version and steps. Report vulnerabilities through [private security reporting](https://github.com/DieDust/FilesMate/security/advisories/new), not public issues or the group.

## Build from source

Use **Windows 11 x64**, **PowerShell 7**, the **.NET 10 SDK** selected by `global.json`, and Windows/WinUI build prerequisites. Packaging additionally needs Inno Setup 6.

```powershell
git clone https://github.com/DieDust/FilesMate.git
cd FilesMate
pwsh ./scripts/build.ps1 -Configuration Release
pwsh ./scripts/test.ps1 -Configuration Release
# Optional: create a self-contained installer
pwsh ./scripts/package.ps1
```

Build in a separate checkout if your installation uses a development output directory. No update-signing private key is needed to build or package. Forks distributing updates must use their own feed and signing identity; see [updates](docs/updates.md).

[Contributing](CONTRIBUTING.md) · [Architecture](docs/architecture.md) · [Localization](docs/localization.md) · [Security](SECURITY.md) · [Release checks and limitations](docs/public-release.md)

## License and credits

[Apache License 2.0](LICENSE). Adapted Files Community UI code and bundled dependencies retain their respective licenses. See [NOTICE](NOTICE), [third-party notices](THIRD-PARTY-NOTICES.md) and [asset provenance](docs/assets.md). FilesMate is independent, not an official release of Files Community or Microsoft.

Screenshots show the actual app using demonstration files. Community artwork was supplied by the maintainer.
