# Tab close memory investigation

The later [bounded-cache and coordinated-reclamation implementation](2026-09-20-tab-memory-balanced.md)
supersedes the fixed collection sequence and cache clearing described below.

## Follow-up: automatic resource reclamation

The initial teardown work below established page collectibility but did not
resolve the user's high working-set observation. The follow-up also detaches
hidden tabs' file-list visuals while retaining selection, layout and scroll state,
opts the executable into SegmentHeap, and schedules a bounded release sequence
after completed tab disposal. Two noncompacting collections are separated by UI
dispatcher turns; a later native heap optimization decommits unused blocks and
requests a composition commit. Consecutive closes are batched and rate-limited.
Pointer, wheel and keyboard activity postpone collection until two seconds of
quiet; active file operations defer it, and window close stops the timer. No
working-set eviction API is used and there is no periodic collection without a
retired page.

Unused dynamic image-cache entries and duplicate decoded thumbnail pixels are
also released. Shared format art and displayed images remain. Folder-preview
requests for surviving views are not cancelled by cache eviction.

The five-cycle automatic-release run (`artifacts/memory-verified-local/tab-memory-smoke.json`)
passed with 51 closed pages and zero survivors, without diagnostic forced GC:

| Stage | Private working set, MiB |
| --- | ---: |
| Cold one tab | 97.3 |
| Immediately after nine tabs close down to one | 189.9 |
| After automatic release | 146.8 |
| Eight-tab cycle 1, closed | 137.8 |
| Cycle 2, closed | 139.6 |
| Cycle 3, closed | 138.1 |
| Cycle 4, closed | 136.8 |
| Cycle 5, closed | 134.5 |
| Final settled one tab | 133.1 |

Selection, scroll, inactive list/sidebar detachment, grid restoration, lazy home
and preview creation, dual panes and unvisited-tab deferral passed. The final
input-deferral revision is separately tested in `artifacts/memory-final-validation`.
App unit suite: 724 passed (`artifacts/memory-verified-app-tests.log`), including
cache eviction while a visible thumbnail request remains in flight.

Collection has a measurable cost: the first nine-to-one release interval added
about 245 ms of cumulative GC pause, spread across collections. Input deferral
and batching avoid collecting during continuous interaction; this is not a
zero-cost operation. Final memory was still 35.8 MiB above cold startup. These
measurements establish improvement and bounded behavior for this workload, not
an exact cold-start return or proof that every remaining native allocation is
necessary. The new production payload is `artifacts/memory-reclaimed-production`.

References: [SegmentHeap](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests#heaptype),
[HeapOptimizeResources](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/nf-heapapi-heapsetinformation),
[private working-set counter](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2).

The final input-deferral revision passed its 19-page UI run with no surviving
closed pages; settled private working set was 136.3 MiB. The production revision
was installed locally at 14:24, with eight payload files hash-verified. Backup:
`artifacts/memory-reclaimed-backup-20260920-142421`; installation manifest:
`artifacts/memory-reclaimed-install.json`. The user's main app was relaunched;
the existing independent SearchHost was left running.

## Initial teardown-only investigation: scope and findings

The report concerns the difference between a freshly started one-tab process and a
one-tab process after opening and closing many tabs. A screenshot alone cannot
distinguish retained pages from runtime/native allocator retention.

The original one-to-nine-to-one UI smoke collected all eight closed page objects.
It nevertheless retained more process memory than at startup. The original smoke
reported `ClosedPagesAlive` without asserting it; the revised smoke fails when
closed pages remain alive. Readiness checks and close operations run in separate
synchronous helpers so diagnostic async state machines do not retain page locals.

The following teardown work was previously deferred to collection:

- The repeater kept its item source and template/recycling association.
- Right-pane and other file-surface callbacks could still refer to the owner page.
- The disposed page kept its visual content and keyboard accelerators attached.
- A disposed pane retained its last published folder index and placeholder names.

The change explicitly disconnects these resources when a tab closes. Both panes
use the same cleanup. Disposal errors now go through the existing diagnostic log.
An empty `DataTemplate` is used when retiring a repeater: this WinUI build rejects
assigning `null` to its item template. The error was caught in isolated testing;
the installed build uses the corrected implementation.

## Validation

- App unit suite: 723 passed, including a behavioral test that retains a disposed
  pane and verifies its folder store, index and placeholder names are released.
- Actual WinUI test: 51 closed pages, zero surviving page weak references. Includes
  closing selected tabs with dual panes and text preview, and verifying immediate
  visual-tree/index release before diagnostic collection.
- Selection/scroll restoration, lazy home/preview creation and unvisited-tab
  deferral also pass.
- Five repeated eight-tab cycles run with normal GC behavior; diagnostic collection
  occurs before/after that sequence only. No forced GC or working-set trimming was
  added to production.
- Raw results: `artifacts/tab-memory-final.json`, `artifacts/memory-app-tests.log`.

The measurements do not establish that all residual native memory is useful cache,
or that closing tabs should restore the exact startup working set. .NET live-object
bytes, total committed managed heap, process private bytes and Task Manager's
private working set are different measures. The longer smoke additionally records
managed heap commitment to help separate these categories.

The extended run completed 25 eight-tab cycles and tested 211 closed pages in
total, with zero surviving page weak references. All functional smoke assertions
passed. Its final diagnostic sample had 5.52 MiB of live managed objects and
10.80 MiB of committed managed heap, versus 305.8 MiB of process private bytes.
A subsequent private-working-set sample was 215.2 MiB. These samples are taken at
different instants and must not be treated as interchangeable memory measures.
The freshly started installed app measured 98.75 MiB of private working set.

Across the extended run, private bytes fluctuated with some upward drift; this
does not prove that native allocations plateau indefinitely. Most residual
process memory lies outside the managed heap. Attribution among WinUI, graphics,
native allocators and other resources remains incomplete. The results establish
closed-page reclamation, not the absence of every native leak or restoration of
the cold-start footprint. Diagnostic windows were closed after testing.

Extended results: `artifacts/tab-memory-long-final.json`,
`artifacts/tab-memory-long-progress.json`, and
`artifacts/tab-memory-long-working-set.json`.

Reference: [Microsoft's WinUI memory guidance](https://learn.microsoft.com/en-us/windows/apps/develop/performance/improve-garbage-collection-performance).

## Local deployment

Built/published in `artifacts/github-public`, without rebuilding the live app output.
The six `FilesMate.App.*` files from a production (`FilesMateUITest=false`) publish
were backed up and copied into the existing local app output while it was closed.
The restarted app responds normally. SearchHost was not restarted.

- Backup: `artifacts/memory-local-backup-20260920`
- Installed file hashes: `artifacts/memory-local-install.json`
- Version unchanged: local 1.1.81 revision; no server or GitHub release was updated.
