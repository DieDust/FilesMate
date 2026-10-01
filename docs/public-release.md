# Public release checks

## 1.1.130-preview.1 — 2026-10-01

- The independent public source export builds with zero warnings and errors. App 1,091, Core 176, Integration 20, and Windows platform 367 tests pass (1,654 total).
- Actual desktop mouse input in the native Shell host passes twelve menu-row hover checks and six clicks across Light/Dark, including opening and selecting a nested View option. Menu hover clears after leaving a row. These checks use the real navigator page at 175% scaling and an isolated profile.
- View choices are ordered Details, List, Large icons. All View labels, including nested choices, start at 28 DIP within their 32 DIP rows. The real toolbar retains an 8 DIP left inset and a 52 DIP New button.
- Native Windows cursor handles and bitmap dimensions match for arrow, hand, text, and both resize directions. Sidebar icons use 20 DIP frames, 4 DIP label gaps, and 32 DIP rows across both themes.
- Native window and child-window wheel checks cover complete-column alignment, fractional deltas, reversal, horizontal input, and popup guards. Window shortcuts pass from five focus locations; text editing, rename cancellation, dialogs, and dual panes retain their intended routing.
- Automatic Name column sizing, manual-width restoration, offscreen names, theme feedback, and column visibility persistence/reset pass in the native UI fixtures. The update also retains thumbnail reuse, folder sizes under icons, folder mutation, pin/checkbox, and consecutive conflict-comparison fixes.
- Test profiles and desktop-input fixtures are isolated from the production app. Release preflight checks compiled test-hook exclusion, source revision, and installer checksums before publishing.

## 1.1.120-preview.19 — 2026-09-30

- The independent public source export builds with zero warnings and errors. App 1,056, Core 173, Integration 20, and Windows platform 365 tests pass (1,614 total). Bounded PDF layout and both resource checks also pass.
- Native UI checks cover 96 menu/radio-button states across light, dark, and theme transitions; 61 browsing/gap-selection cases; and nine navigation-position cases. Breadcrumb drops and archive-drop conflict replacement/undo use isolated files and the production transfer handlers.
- Release preflight corrected the settings-search targets for the compact font-size controls, updated obsolete layout assertions, and bounded retries for transient sharing violations in the directory-lifecycle test. The producer-disposal timeout remains enforced.
- The production installer disables UI-test entry points and excludes test profiles. GitHub Releases and the signed update feed distribute the same installer; its checksum is published in the release assets.

## 1.1.119-preview.9 — 2026-09-28

- The public source export builds independently with zero warnings and errors. App 969, Core 155, Integration 20, and Windows platform 354 tests pass (1,498 total).
- Search UI checks pass in light and dark themes for Simplified Chinese, English, and Japanese, including category selection, scrolling with many categories, selected-category visibility, compact results, and centered window placement.
- README images were recaptured from the new skin using demonstration files and isolated profiles, including two/three-pane layouts, search, media thumbnails, document previews, and settings.
- The installer is the locally reviewed preview.9 package: 86,183,425 bytes, SHA-256 `323F01869146DCA5E7FF20AB53222FE277F600E1F4C4980B709DD52DB5DD2F1C`. The documentation capture harness is test-only and does not ship in it.

## Initial public source snapshot

Source baseline: FilesMate 1.1.81-preview.20260920, reviewed on 2026-09-20. This is a preview release, not a security certification.

## Checks performed

- Clean source export built in a separate directory using the documented Release build/test scripts: zero build errors; App 722, Core 127, Integration 20 and Windows platform 169 tests passed (1,038 total). Performance-test placeholders are excluded and are not presented as benchmark evidence.
- Gitleaks 8.30.1 scanned 169 local development commits with no detected secrets. The scanner binary was checked against its release checksum. The public repository starts from a clean snapshot, excluding local development history, screenshots from personal sessions, temporary artifacts, agent notes and operational server configuration.
- NuGet's direct/transitive vulnerability query reported no known vulnerable dependencies on this date. Advisory coverage is not proof that dependencies have no vulnerabilities.
- Public release preparation includes a second secret scan of staged source, documentation link checks, third-party notice review, and a Windows CI build/test workflow.
- README screenshots were captured from the actual app with demonstration files and a separate profile. The group invitation was supplied by the maintainer.
- The 1.1.81 installer had already been installed locally: exit code 0, 1,519 payload files verified, 17 settings files preserved. The official online update was checked using the 1.1.65 client, including signature, full download, size/hash verification, localized notes, HTTP 206 range requests and HTTP 200 health response.

Installer SHA-256: `00F1CB610146AF53D70755831A56C515ACD6D504FEDD157AFB954FF1A3D966A9` (85,089,801 bytes). The installer is a previously tested production package; development-only test harness changes do not ship in it.

## Known limitations

- Windows 11 x64 only in this installer. Windows 10, ARM64 and non-Windows platforms are not supported.
- Search is filename/application search, not full-document content search; the file manager maintains the index. Optional background search consumes memory even after the main window closes.
- File operations can finish partially on failure or cancellation. There is no crash-resumable transaction journal. Permanent deletion cannot be undone; replacement backups use disk space and retention limits.
- Native shell extensions, Office parsers and preview workers are not an OS security sandbox. Some Office compatibility previews require installed software. See [security boundaries](../SECURITY.md).
- Performance budgets in `performance.md` are targets, not certified measurements across machines. No universal latency or memory claim is made by the README.
- Signed update manifests authenticate packages but do not substitute for Windows Authenticode signing. Windows may show reputation or publisher warnings for preview installers.

Report reproducible problems through GitHub Issues; send exploitable vulnerabilities through the private reporting channel in SECURITY.md.
