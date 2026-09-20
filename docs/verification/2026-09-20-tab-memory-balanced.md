# Tab memory: bounded caches and coordinated reclamation

A subsequent [user-settings reproduction and finalization follow-up](2026-09-20-user-profile-memory.md)
updates the native-owner tracking, bounded final pass and retained-view scroll
restoration described here. This file retains the earlier experiment results.

This implementation follows [the file-manager research](2026-09-20-file-manager-memory-research.md)
and supersedes the fixed two-pass, per-window reclamation described in
[the earlier investigation](2026-09-20-tab-memory.md).

## Implementation

- Closed pages cancel work, detach their visual trees, remove callbacks and release
  both panes' data. Weak references track retired page and visual-tree wrappers;
  the coordinator never owns those objects strongly.
- One coordinator serves all windows. Consecutive closes coalesce. Collection
  waits for two seconds without input and defers during file operations, active
  directory loading, dragging, pointer capture and inline rename. New batches
  respect a ten-second collection interval.
- If retired wrappers remain, request an optimized, noncompacting collection.
  If it does not start a generation-2 collection, request a noncompacting forced
  collection. A second check occurs four seconds later, with at most two passes
  per batch. Native heap optimization follows; no EmptyWorkingSet call is used.
  `blocking: false` does not eliminate GC pauses, especially with WinRT wrappers.
- The active tab and one recently visited tab retain file-list visuals. Older
  background lists detach visuals while retaining selection, scroll position
  and layout. Existing idle-tab suspension settings remain available.
- Shared dynamic image entries use byte-bounded LRU eviction. Closing one tab
  does not clear shared thumbnails or cancel other views' preview requests.
  Dynamic images, raw thumbnail pixels and folder covers have respective budgets
  of 24, 8 and 8 MiB (previously 64, 16 and 32 MiB). These are cache budgets, not
  a process-memory limit; live UI images and native allocations are additional.
- A thumbnail cache hit is displayed directly. It must never be inserted into
  the separate bundled-format-art cache, which would bypass dynamic eviction.

## Experiments and interpretation

UI-test builds run outside the installed application's directory, with isolated
settings and generated fixture files. The independent SearchHost is excluded.
Measurements use private working set, private committed bytes, managed heap,
weak page references, handles, GC pause totals, dispatcher heartbeat gaps and
thumbnail counters. MiB means 1,048,576 bytes. No diagnostic forced-GC loop or
working-set trimming is enabled for the acceptance runs.

The preliminary two-cycle text trials rejected natural collection and one
optimized-only request: each still retained a closed page at final measurement.
Tracking only the page wrapper reclaimed less native UI memory than tracking
the page, its root and its two file surfaces. The selected implementation uses
the latter evidence to decide whether another pass is needed.

### Text and multiple windows

`artifacts/memory-balanced-final/tab-memory-smoke.json`: passed, 53 closed pages,
zero surviving closed pages. Five eight-tab cycles ended at 140.1, 142.4, 142.3,
145.3 and 137.2 MiB private working set; after preview/dual-pane checks the process
settled at 134.1 MiB. Multiple-window checks verified that an active file-operation
lease and continued input defer collection, and that both windows share no more
than two collection requests for the tested close batch.

Twenty warm-tab switches measured 40–141 ms to a dispatcher yield and forced
layout completion. This is not end-to-end click latency or a frame-time result.
Dispatcher gaps still occurred during setup and resource collection, so the run
does not establish that pauses have been eliminated. The final media build also
contains the dynamic-cache-hit correction; this text run did not load thumbnails.

### Media grid

`artifacts/memory-balanced-media/tab-memory-smoke.json`: passed, 20 eight-tab
cycles, 171 closed pages, zero surviving pages. Fixture: 100 text files, 24 generated
images, eight generated video clips and eight folders with image covers. Private
working set was 111.4 MiB on initial display, 155.0 after the first cycle,
161.8–172.7 over the final ten cycles, and 156.1 after preview/dual-pane checks.
Thumbnail decoding stayed at 37 successful loads throughout, with 5,757 dynamic
cache hits by the last sample. Visible media were reused without repeated decoding.

The largest recorded dispatcher gap was 626 ms and the largest sampled most-recent
GC pause was 451 ms. These include repeated tab creation, layout and natural GC,
not solely the idle coordinator. They rule out claiming a pause-free result;
there is no equivalent old-build media run establishing a latency improvement.

An initial detached-process launch ended after cycle four without a final report,
managed crash record or Windows Application error. Its cause is unconfirmed and
that run is excluded from acceptance. The rerun retained a process handle through
the launching session and completed all 20 cycles. The interrupted observations
are retained in `artifacts/memory-balanced-media/interrupted-progress.json`.

### Large directory

`artifacts/memory-balanced-large/tab-memory-smoke.json`: passed, 5,000 generated
text files, 20 eight-tab cycles, 171 closed pages and zero survivors. Private
working set was 101.0 MiB initially, 147.2–152.4 MiB over the final ten cycles,
and 143.5 MiB after preview/dual-pane checks. The largest sampled dispatcher gap
was 1,054 ms across setup, repeated loading/layout and reclamation; this is a
remaining interaction-performance limitation, not evidence of pause-free browsing.

### Automated regression coverage

App test suite: **726 passed, 0 failed**. Includes byte/entry-budget eviction,
hot-entry retention, replacement accounting, oversize rejection, and folder
preview eviction without cancelling another active request.

UI checks cover selection/scroll/layout restoration, recent and older tab views,
lazy home and preview creation, deferred unvisited tabs, dual panes, selected-tab
close, and repeated page collectibility. The file-operation deferral test holds
the real lifetime lease; it does not perform a production copy or alter the user's
clipboard. Video thumbnails are exercised separately from video playback.

## Tradeoffs and limits

Keeping a recent tab ready spends some memory to avoid rebuilding it. Returning
to an older detached tab requires layout reconstruction. Reducing cache budgets
can cause additional thumbnail decoding when a working set exceeds those budgets.
Conditional GC still has CPU and pause costs. Cold-start memory is not the return
target because initialized framework resources and useful bounded caches persist.

Repeated-cycle stabilization and page collectibility do not prove that every
native or GPU allocation is leak-free. This run does not include a full native/GPU
heap attribution, long video playback, or every shell thumbnail provider.

## Local installation

Production publish used `FilesMateUITest=false` in the isolated public checkout.
The UI-test control string is present in the test binary and absent in production.
Eight App/Platform files were copied into the existing installation after graceful
shutdown, with SHA-256 verification. Existing user settings were retained.

- Backup: `artifacts/memory-balanced-backup-20260920-153051`
- Manifest: `artifacts/memory-balanced-install.json`
- Restarted main PID: 2612; independent SearchHost PID 42892 was left running.
- Version unchanged; this is a local test revision, with no online release.
