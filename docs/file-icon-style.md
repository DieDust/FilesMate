# File icon style and backup navigation (1.1.80)

Appearance → **Use FilesMate file icons** is enabled by default, including existing profiles without the new setting. Turning it off uses Windows Shell icons for associated applications. It changes file artwork, not file associations or the application theme. Image/video thumbnails and explicit folder covers remain available.

The setting is stored as `useBundledFileIcons` in `appearance.json`. Live image bindings are tracked with weak keys; switching rebinds existing list/grid/shelf images without rescanning folders. Each asynchronous request retains its style and binding stamp, so a late result cannot overwrite the current style. Drag previews and favorites also follow the setting. The independent search host reloads the same preference when its window is shown and cancels pending icon requests before refreshing visible results.

Backup-management location buttons open a new tab in the originating FilesMate window. The backup dialog and settings overlay close first. No shell process or folder-association change is involved; missing/offline locations show an error in the dialog. There is no separate backup-opening preference.

Validation on 2026-09-19:

- App tests: 722 passed, including default/migration/persistence and restoration of the icon preference without changing other appearance settings.
- Convenience UI: all 26 checks passed. New checks verify bundled SVG → Windows bitmap → bundled SVG, drag-preview consistency, rapid toggles, and clicking a backup location to open the exact folder in a new FilesMate tab. The tab-lifetime check found zero retained closed pages after diagnostic collection.
- Independent SearchHost UI smoke passed: native file icon loading, restoring bundled artwork, and reading new/old profile settings.
- Both UI builds completed with zero warnings/errors. On this computer, the native PDF icon resolved to WPS.

Evidence is in `artifacts/icons-app-tests.log`, `artifacts/icons-ui-smoke.json`, `artifacts/icons-search-smoke.txt` and `artifacts/file-icons-{bundled,system}-1.1.80.png`. UI tests use isolated profiles and disposable files.

Installed `1.1.80-preview.20260919` locally and reopened FilesMate. Installer exited with code 0; all 1,519 payload files matched and all 17 existing profile JSON files were preserved (`artifacts/upgrade-1.1.80/verification.json`). The public update feed was not changed.
