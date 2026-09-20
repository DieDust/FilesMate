# Initial public release checks

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
