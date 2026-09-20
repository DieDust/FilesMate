# Focused safety, interaction and performance review — 2026-09-19

Scope: update verification/installation, local file creation and undo ownership, search IPC and action handoff, document previews, decoded thumbnail caches, and update error interaction. This is a source review with regression tests and local validation, not a penetration test or a claim that all vulnerabilities have been eliminated.

## Findings addressed in 1.1.73

| Priority | Finding | Change and verification |
| --- | --- | --- |
| High — data safety | `CreateEmptyFile` used `File.Create`. A file appearing after the UI's availability check could be truncated. | Use `FileMode.CreateNew`. A regression test failed on the old implementation and now verifies that existing contents survive. |
| High — data safety | New-folder actions used idempotent directory creation, so a concurrent directory could be incorrectly treated as newly created and entered into undo history. | Use exclusive Windows directory creation for new-folder/grouping actions. Keep idempotent parent creation for undo. Concurrent creation and existing-content tests cover both semantics. |
| Medium — IPC hardening | The search client did not verify server ownership; replies used unbounded `ReadLineAsync`. The server already restricted clients to its user. | Add client-side `CurrentUserOnly`; bound requests to 4,096 characters and replies to 8,192; reject oversized and truncated frames. A real named-pipe regression reproduced the oversized reply problem. Cross-account behavior is based on the documented API and was not tested with a second Windows account. |
| Medium — operation consistency | Search action files were read and then deleted in separate steps. Concurrent consumers could both obtain the same action. Size validation also preceded opening the file. | Exclusively open each request with `DeleteOnClose`, validate size on the locked stream, deserialize, then consume. Reject oversized requests before writing them. Concurrent-consumer tests allow only one successful claim. |
| Medium — memory accounting | Thumbnail cache entries, eviction queue and byte count were independently modified during clear/load races; only pixel bytes bounded the cache. | Reuse the existing locked LRU with both a 16 MiB decoded-byte budget and a 4,096-entry limit. Concurrent clear/load tests verify retained bytes, subsequent eviction and empty-cache accounting. This bounds the cache, not total process memory or in-flight decoder allocations. |
| Low — localization | Update failure used the glass-effect “Off” label for the close button. | Use the existing localized `Close` string. |

## Controls inspected

- Updates use an HTTPS feed, disable redirects in the production HTTP client, verify RSA-PSS/SHA-256 signed manifests, limit manifest/package sizes, and compare installer bytes to the signed hash again while excluding write/delete sharing before launch. Version downgrade and busy-operation checks remain in place.
- Terminal launch passes folder data separately; PowerShell fallback encodes the path rather than concatenating untrusted path text into executable script.
- Existing file-copy/delete code and tests cover reparse-point behavior, nested destinations and batch-rename rollback. This review does not establish immunity to all filesystem namespace races.
- Markdown rendering disables embedded HTML and scripting and uses a restrictive CSP. Office HTML blocks network resources; the PDF viewer has a restrictive CSP and disables PDF.js evaluation. WebView permissions and downloads are denied. These controls do not prove that parsers or installed third-party preview handlers have no vulnerabilities.
- NuGet's known-vulnerability scan, including transitive packages for the solution, reported no matches on this date. Bundled PDF.js, Windows, WebView2 and installed Office/WPS components are not covered by that NuGet result.
- A filename scan and private-key-header scan of tracked files found no tracked signing key/private-key header. Signing-key files are ignored by Git. This was not a complete historical or entropy-based secret audit.

## Validation and evidence

Regression output is retained locally under `artifacts/test-results/security-review/`; the dependency scan is `artifacts/security-dependencies.json`. These generated files and machine-specific paths are not included in source control.

- All 939 tests passed: App 712, Windows platform 109, Core 118. Coverage includes real named-pipe exchange, Unicode framing, malformed/oversized messages, one-use action consumption, safe creation and concurrent cache accounting.
- The release installer built successfully. The UI test build completed with zero warnings and zero errors.
- An actual packaged SearchHost ran with an isolated profile. After oversized input, invalid JSON, a truncated stop request, empty disconnect and a stalled-client timeout, the original PID still answered status requests. An explicit complete stop request then exited cleanly. Evidence: `artifacts/security-searchhost-smoke.json`.
- All 13 UI interaction checks passed, including inline rename/Tab, clipboard non-overwrite behavior, grouping/undo with later-created content retained, drag dwell cancellation, cross-pane transfer, preview selection, closed-tab restoration and hibernation. Evidence: `artifacts/security-ui-smoke.json`.
- The hibernation diagnostic retained zero sleeping page objects after diagnostic GC. That diagnostic does not establish lower steady-state working set or long-duration memory stability; production does not force GC.
- Version `1.1.73-preview.20260919` was installed locally with installer exit code 0. All 1,519 installed payload files matched the packaged hashes, and all 17 existing JSON settings files retained their hashes during installation. The installed main application was reopened. Evidence: `artifacts/upgrade-1.1.73/verification.json`. This release was not published to the public update feed.

No additional polling worker, watchdog or per-frame processing was introduced. Cache lookups now briefly share the same lock as cache mutations; file decoding remains outside that lock. Throughput and long-duration process memory are separate measurements, not inferred from a passing cache test.

## Remaining boundaries

The independent Office parser/preview process still runs with the current user's privileges; process separation and timeout/memory monitoring are **not** an OS security sandbox. Native WPS/Office preview behavior remains dependent on installed handlers. A restricted-token/AppContainer design needs a separate compatibility investigation before it can be claimed as protection for hostile documents.

IPC ownership protects the Windows-user boundary, not against other malicious code already running as the same user. No claim is made that these changes remediate a known remote-code-execution issue. The existing dedicated performance-test project contains a skipped placeholder, so it is not a working performance release gate.

Reference: Microsoft documents the server and client ownership checks of [`PipeOptions.CurrentUserOnly`](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0). The production pipe server remains independently resident when the main window closes.
