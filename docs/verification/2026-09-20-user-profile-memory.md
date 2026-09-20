# Memory follow-up using the user's window and settings

## Deployment verification

The previous local installation was present: all eight App/Platform file hashes
matched `artifacts/memory-balanced-install.json`. Start-menu and taskbar shortcuts
targeted that same directory. The main app was not running when this follow-up
started; the independent SearchHost remained running. Reopening the installed app
gave approximately 118–122 MiB private working set after startup. This does not
measure the user's earlier close-tab sequence and cannot disprove their report.

## Coverage gap and reproduction

Earlier acceptance runs primarily used a smaller isolated window and repeated
opens of one generated folder. This follow-up copies the user's appearance,
explorer, window-placement and pinned-location settings into an isolated profile.
The larger window uses layered acrylic and the user's transparency setting.

`artifacts/memory-user-settings/tab-memory-smoke.json` reproduces a slow/incomplete
first return: 242.3 MiB immediately after closing down to one tab, 200.0 after idle,
and 162.4 after another open/close cycle. Closed page weak references were already
dead, while native ownership and finalization were still relevant. Counting only
page disappearance is insufficient to decide that native cleanup has completed.

An additional scenario opens Home before entering folders, rotates through actual
local directories and retains the generated fixture as the final selected tab.
These operations only enumerate directories; they do not modify their contents.
Per-pass test logs record resource survival, collection counts and busy deferrals.

## Implemented changes

- Track XAML projections and their `IWinRTObject.NativeObject` owners using long
  weak references. The references are inspected only for liveness; finalized
  objects are never accessed or resurrected. A normal short weak reference can
  disappear before finalization. See [Microsoft weak-reference semantics](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/weak-references)
  and [CsWinRT COM-reference ownership](https://github.com/microsoft/CsWinRT/blob/master/docs/interop.md).
- Permit one final collection when two previous passes have reduced the number
  of surviving retired resources. Stop after at most three passes, or earlier
  when resources are gone; lack of progress never creates an unbounded loop.
  Keep input, file-operation and loading deferrals. Passes are separated by two
  seconds to allow dispatcher and finalizer work.
- Commit detached composition visuals before optimizing unused native heap
  blocks. No working-set eviction API is used.
- Save and restore scroll position for retained recent views as well as detached
  views. A real-settings run exposed a grid restoration assertion failure; the
  previous retained-view path depended on the unloaded ScrollViewer preserving
  its native offset by itself.
- Explicitly release a closed Home dashboard's repeaters, template pools, search
  results, content and parent event handlers. Invalidate asynchronous reloads and
  reject late refresh/customization callbacks on the released dashboard.

Preliminary native-owner tracking alone did not eliminate high residual memory.
The same Home/folder scenario measured 240.4 to 182.3 MiB with that candidate,
and 235.5 to 172.2 MiB with the progress-limited final pass. Those are single runs,
with different warmed framework state, not a statistically established speedup.
The latter run failed the scroll-state check and was not installed.

## Final UI results

`artifacts/memory-home-teardown/tab-memory-smoke.json` passed with the user's
settings, Home-first navigation, three rounds of eight tabs across real folders,
preview/dual-pane checks and multiple windows. All 37 tracked closed pages were
collected. Selection and scroll restoration passed. Active file-work and input
still defer process-wide reclamation; the tested batch stayed within three passes.

Private working set (MiB): immediately after closing to one tab 241.4; after idle
175.6; subsequent rounds 167.8, 167.5, 169.2; after preview/dual-pane checks 160.0.
The 267.1 MiB multiple-window sample includes the still-open second window and is
not a one-tab comparison. Asynchronous cleanup is observable in the per-pass
`tab-reclamation.jsonl` file. The test does not promise an immediate return to the
cold-start footprint or eliminate all pauses.

The final source additionally guards late Home customization callbacks after
release; those guards do not affect the measured normal UI sequence. The local
revision is `1.1.81-preview.20260920.2` so About can identify it distinctly from
the previous build.

## Local deployment

The final App test suite passed: 726 tests, zero failures. A production publish
with `FilesMateUITest=false` was built in the isolated checkout. All 12 root
FilesMate App/Core/Platform/Search binary, symbol and metadata files were backed
up and replaced after the running main app closed gracefully. SearchHost remained
running separately and its files were not replaced.

- Version: `1.1.81-preview.20260920.2`
- Installed main PID: 49372; observed window title: Home (Chinese UI)
- Backup: `artifacts/memory-user-backup-20260920-160134`
- Hash manifest: `artifacts/memory-user-install.json`
- No server or GitHub release was published.

## Validation boundary

The aim is observable reclamation after closing tabs without discarding settings
or useful active-tab state. Framework/native caches can remain above cold-start
memory. Conditional GC can still pause interaction. A lower memory observation
does not establish that every native allocation is released or that the user's
unrecorded sequence is fully resolved.
