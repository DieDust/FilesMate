# Window responsiveness — 2026-09-20

The reported symptoms were slow continuous window movement and an unresponsive
navigator while activating `Uninstall Snow Shot.exe`. The real uninstaller was
not run again during verification.

## Findings and changes

- `AppWindow.Changed` queued a synchronous position save at low dispatcher priority.
  Each processed position event could serialize JSON, write a temporary file and
  replace `window.json` on the UI thread. Low priority did not debounce movement.
- In a native-host baseline, 60 position samples caused 59 observed file updates.
  The new build performed zero during movement and saved the final position after
  movement settled. A 400 ms one-shot timer covers programmatic movement and the
  ordinary WinUI host. Native move/size messages suspend saving until release.
- Normal bounds are captured before the delayed save, preserving maximize/restore.
  Closing flushes the latest bounds. Native dragging also prevents idle tab
  reclamation from treating a held caption as inactivity.
- File activation called synchronous `Process.Start(UseShellExecute=true)` directly
  from the navigator event handler. It now awaits the existing background STA
  shell worker, disposes the returned process handle and reports errors to the
  originating pane. Repeated activation of the same path on the page is suppressed
  while activation is pending. Closing a tab does not cancel an already requested
  external launch.

Microsoft documents that ShellExecuteEx can invoke shell extensions and that some
extensions require STA: [ShellExecuteExW documentation](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw).
The file-launch change removes a confirmed UI-blocking call path; no valid stack
dump of the original uninstaller incident was obtained, so a specific third-party
extension or security scanner has not been identified as its trigger.

## Validation

- Release production installer built successfully as 1.1.83.
- Installed locally: setup exited 0; all 1,519 payload files matched and all 17
  saved JSON settings remained unchanged during installation. Main app and the
  previously running SearchHost were reopened.
- 18 targeted application checks passed (placement, minimize, title-bar and motion
  contracts); 2 STA shell-worker checks passed, including a blocked worker that
  does not block the caller or another operation and exception propagation.
- `scripts/test-window-placement-drag.ps1` passed for native and ordinary WinUI
  hosts: zero observed writes across 60 move samples; final bounds, quick
  resize/maximize/restore, idle behavior and close-before-delay persistence passed.
- Two overly broad animation assertions were narrowed to animation construction;
  the placement timer is explicitly registered as a non-animation duration.
- Final release checks passed: 726 application tests, 170 Windows platform tests,
  127 core tests and 20 integration tests (1,043 total). This supersedes the
  interrupted exploratory run before the non-animation timer assertions were
  corrected.

## CPU and movement comparison

An isolated UI-test baseline and changed build were each moved through the same
180-position sequence using `SetWindowPos` with `SWP_NOSIZE`, at 1700 × 1000 pixels,
starting at (300, 200), using the user's appearance settings (0% transparency).
`dotnet-trace` sampled the first nine seconds. The automation includes a sleep per
position; its total duration is not a frame-rate measurement or a mouse-latency
measurement.

| Measurement | Baseline | Changed |
| --- | ---: | ---: |
| Total sequence duration | 21.375 s | 6.777 s |
| Process CPU time consumed | 2.141 s | 1.375 s |
| Idle CPU time per second | 0 ms | 0 ms |

Baseline UI-thread samples repeatedly contained `WindowPlacementService.Save`
and `File.Replace`. Those calls no longer dominate the changed trace. The mean
CPU percentage during the shorter changed run is higher despite lower total CPU
work; do not present the result as a guaranteed decrease in Task Manager's
instantaneous CPU percentage. Native drawing/compositor cost and real pointer
latency were not fully attributed by these managed thread-time samples.

Local raw evidence (ignored, not shipped): `artifacts/drag-*-normalized.json`,
matching `.nettrace` files, `artifacts/profile-window-drag.ps1`, and
`artifacts/local-upgrade-1.1.83/install-verification.json`.
