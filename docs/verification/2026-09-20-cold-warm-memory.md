# Cold versus warmed single-tab memory

Investigation date: 2026-09-20. Starting installed revision:
`1.1.81-preview.20260920.3`.

## Question and method

Opening several directories raises memory. Closing them returns the UI to the
original directory, but does not return the process to its startup footprint.
The comparison must use the same process and original directory, with the same
per-folder layout preferences. Restarting the app is not a reclamation result.

An isolated test build copied the user's appearance, navigation and per-folder
preferences. It started with the build-output directory in details view, then
opened four additional tabs alternating that directory and the user's video
directory (saved grid layout). It closed those tabs, waited 15 seconds, and
repeated three times. A control used only the details directory.

Measurements include private working set, private commit, GC live-size estimate,
GC committed bytes, native `HeapSummary` allocated/committed bytes, image-cache
counters, page weak references, modules, and virtual allocation ranges. The
observer is run twice before browsing to expose its startup cost. Private
working set uses `QueryWorkingSetEx`, excluding shared resident pages.

Heap dumps, heap walking and allocation-stack instrumentation were separate
diagnostic runs. Reading memory pages can increase their residency. Their
working-set totals are not used as product before/after evidence. Dumps and raw
traces remain local under ignored `artifacts/`; they may contain private data.

## Findings before the new change

All values below are MiB. Commit and allocation totals are different measurements
from resident memory; they must not be added to the working-set total.

| Same process, same remaining directory | Startup | After three mixed-layout rounds |
| --- | ---: | ---: |
| Private working set | 105.9 | 160.8 |
| Private commit | 179.3 | 250.3 |
| GC committed | 9.3 | 35.4 |
| GC used estimate | 3.8 | 13.0 |
| Native heaps allocated | 79.4 | 83.2 |
| Native heaps committed | 85.3 | 108.9 |

1. **The closed page count returned to zero.** A separate warm heap dump had
   one `NavigatorPage`, 80 `FileRow` wrappers, and no instantiated `FileTile`.
   This supports release of those test pages; it does not prove every native
   allocation in the process has been released.
2. **The managed heap stayed enlarged.** Roughly 16 MiB of GC fragmentation
   remained in the baseline. After diagnostic cache clearing and two ordinary
   compacting collections, the live-size estimate fell to 5.45 MiB but committed
   GC memory remained 26.74 MiB. An aggressive decommitting collection in a
   separate run reduced GC commit from about 27 to 7.8 MiB. Ordinary collection
   and decommitting unused heap capacity are distinct outcomes.
3. **Native heap capacity also stayed enlarged.** Actual native allocations were
   close to startup, while unused committed native capacity grew from roughly
   6 to 26 MiB. Additional `HeapOptimizeResources` calls did not recover this
   entire difference. Heap walking confirmed live allocation blocks in the
   new heap regions, consistent with space that cannot all be discarded as one
   empty region.
4. **Shared thumbnails contributed several MiB.** Raw-thumbnail and folder-cover
   counters each reported 5,564,160 bytes. They can reference the same bitmap
   arrays, so adding those two counters would double-count shared data. Clearing
   every image cache was a diagnostic experiment, not the proposed production
   behavior.
5. **Code loading did not explain tens of MiB.** CLR loader-heap commit grew only
   about 0.73 MiB in the paired dumps. Resident private image-page growth in the
   untouched baseline was about 0.08 MiB. Thread count did not grow continually.

The details-only control went from 105.7 to approximately 131–135 MiB. Thus
ordinary tab construction also expands heaps; media/grid work adds further
allocation and cache pressure. `DOTNET_GCConserveMemory=5` did not materially
improve the mixed-layout sequence and was not adopted.

Native `HeapSummary` and GC totals do not explain every private allocation.
Before the fix their combined commit growth accounts for roughly 49.5 MiB of
the 71 MiB private-commit increase. One other region grew by about 16 MiB. Its
allocation owner requires separate tracing; zero-filled bytes or pixel-like
values alone do not establish that owner.

A local diagnostic DLL successfully mapped native heap blocks and captured
KernelBase allocation stacks, including CLR commits. It did not identify that
remaining region. A broader import-hook experiment stalled its isolated test
process and was terminated. The experimental DLL and its instrumented source
remain in ignored artifacts; all hooks and DLL imports were removed from the
repository diagnostic class. None of this instrumentation is in the installed
production build. This limitation must not be converted into a claim that all
remaining native memory is a harmless rendering cache.

## Targeted production changes

The existing process-wide close/hibernate reclamation queue now considers a
decommitting collection after its bounded wrapper-release passes and the
compositor commit. It requires all of the following:

- At least two seconds without input, no active file operation or busy view.
- At least 8 MiB and 25% unused managed capacity, estimated from current used
  bytes and the last GC committed count.
- At most 64 MiB of estimated live managed data, to exclude large live datasets
  from this blocking collection path.
- At least 30 seconds since the previous decommit. A close during that interval
  is coalesced for reconsideration at the end of the cooldown.

The asynchronous compositor wait rechecks activity and the close-request
generation before acting. There is no idle periodic GC loop, working-set
eviction, indiscriminate cache clearing, or wait for finalizers on the UI thread.
The aggressive collection itself is blocking. Initial isolated measurements
were 20–26 ms; automatic close-batch measurements were about 22–47 ms. This is a
measured cost, not a promise of zero latency on other machines or workloads.

The UI regression also reproduced an existing intermittent scroll restoration
failure after discarding a background view. Restoration previously clamped the
saved offset while the recreated repeater still had an empty scrolling extent.
It now waits for the measured content extent to reach the ScrollViewer. The
pending restoration is removed on navigation, disposal, unload or user input.
The regression checks the actual `ScrollViewer.VerticalOffset`.

## Validation

In the untouched initial decommit comparison, the mixed-directory process
started at 104.9 MiB. After closing back to one tab it reached 142.3 MiB in round
one and 141.4 MiB in round three, versus 155.2 and 160.5 MiB in the baseline.
Round two initially missed decommit during the cooldown; the final implementation
retains a single pending reconsideration rather than dropping the request.

The full UI regression passed with 21 closed pages checked, actual scroll and
selection restoration, retired grid release, lazy preview/Home creation,
deferred unvisited tabs, and multi-window reclamation. In that fixture workload,
private working set fell from 205.5 MiB immediately after closing to 131.5 MiB
after idle, and later settled at 125.7 MiB. The failed scroll run is retained
alongside the passing run; it was fixed rather than counted as a pass.

The app unit suite passed 726 tests with no failures after the production source
changes. These bounded reproductions do not establish an indefinite memory
ceiling or guarantee a return to cold-start memory after every workload.

## Local installation

Revision `1.1.81-preview.20260920.4` was installed at 18:26 on September 20.
The main application was closed normally, its twelve application files and
current window session were backed up, and every replacement file was verified
against the publish output with SHA-256. Metadata inspection of the installed
payload confirmed that `Program.IsUiTestBuild` returns false. SearchHost retained
its original process. No public release or remote push was made in this task.

The production test uses UI Automation and external process counters; it does
not load the diagnostic DLL or force extra collections. Six rounds each open
four tabs alternating the same two user directories, close them, wait 15 seconds,
and sample the same remaining directory. The user's original tab session is
restored at the end. Raw results are in
`artifacts/memory-installed-decommit-measurements.json`.

| Installed process, one tab | Private working set (MiB) |
| --- | ---: |
| Before browsing | 98.41 |
| Round 1 after idle | 141.26 |
| Round 2 after idle | 135.47 |
| Round 3 after idle | 138.61 |
| Round 4 after idle | 139.89 |
| Round 5 after idle | 140.62 |
| Round 6 after idle | 142.42 |

The sixth close fell from 177.50 to 142.42 MiB in 15 seconds. No restart occurred
between measurements. These six rounds stayed in a 135–143 MiB band after idle;
they do not demonstrate that arbitrary browsing can never exceed that band.
The approximately 40 MiB difference from cold start has not been eliminated.

## References

- [GCCollectionMode: Aggressive requests decommit](https://learn.microsoft.com/en-us/dotnet/api/system.gccollectionmode?view=net-10.0)
- [Native allocated and committed heap totals](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/ns-heapapi-heap_summary)
- [WinUI garbage collection guidance](https://learn.microsoft.com/en-us/windows/apps/develop/performance/improve-garbage-collection-performance)
- [Private/shared resident page flags](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-psapi_working_set_ex_block)
