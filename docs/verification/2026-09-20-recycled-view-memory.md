# Recycled file-view memory investigation

The user still observed 210.6 MB with one tab in revision
`1.1.81-preview.20260920.2`. The live process was sampled before restarting:
207.86 MiB private working set, 306.38 MiB private commit. Its dynamic image cache
held 767,872 bytes. A heap dump contained one active page and an already disposed
page with no managed roots reported by SOS; this alone does not account for native
XAML ownership. The dump and diagnostic files remain under ignored `artifacts/`.

## Reproduction and cause

Previous tests copied general preferences but omitted per-folder view settings.
The user's video directory has a saved grid layout, while the build-output
directory uses details. The new reproductions copy `folder-customizations.json`
and its view-scope file, allow previews to settle, and exercise both layouts.

WinUI's `ItemTemplateWrapper` stores recycled controls in a pool associated with
the template, and the repeater retains recycled visual children. Assigning an
empty template does not detach the old children from a live repeater. Replacing
the resource templates alone also failed the new lifetime test.

Source: [WinUI ItemTemplateWrapper](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/Repeater/ItemTemplateWrapper.cpp)
and [ItemsRepeater](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/Repeater/ItemsRepeater.cpp).
These implementation details were validated against the installed SDK through
the before/after UI test, rather than assumed from the development branch.

## Change

Retire the repeater and its owned templates together when switching between grid
and details, or discarding an older inactive view. Detach its event handlers,
clear its bindings and visual parent, then use a fresh repeater with compiled
templates. Keep selection and scroll state on `FileDetailsSurface`. A permanently
closed surface also releases its templates and content. Current and recent tab
switches continue reusing their view; new containers are needed when changing
layout or restoring an evicted view.

No extra production GC passes or working-set eviction were added. Diagnostic GC
in the lifetime test is compiled only into the test build.

## Evidence

- Before: `memory-template-lifetime-baseline.json` fails because the old grid's
  tiles survive four diagnostic collections while details remains displayed.
- After: `memory-template-lifetime-fixed.json` confirms 140 grid tiles released.
- Three navigation rounds, with one tab remaining after each round:

| Private working set (MiB) | Before | After |
| --- | ---: | ---: |
| Initial directory | 105.9 | 104.7 |
| Round 1, after close and idle | 143.7 | 137.8 |
| Round 2, after close and idle | 154.1 | 141.1 |
| Round 3, after close and idle | 158.1 | 147.0 |

These are individual controlled runs, not a universal memory ceiling. Reclaiming
the retired view does not unload all framework, rendering or codec allocations.
The user's original 210.6 MB observation must not be compared with a fresh
restart and presented as a measured reduction.

The App unit suite passed 726 tests with zero failures. The full UI regression
passed with 21 closed pages checked, selection and scroll preserved, lazy preview
and Home available, deferred unvisited tabs, and multi-window reclamation checks.
An earlier full run failed the evicted-view scroll assertion; two isolated state
checks and the final full run passed without a production code change. This
intermittent result remains worth monitoring; it is not evidence of a scroll fix.
The test failure message now includes actual and expected offsets and selection.

The broader run starts in a media fixture and includes repeated Home visits. Its
private working set went from 244.1 MiB immediately after closing eight tabs to
210.1 MiB after idle, and 203.0 MiB after the next close cycle. Closed pages were
collected. These results show that the retained-control fix does not by itself
guarantee a process footprint below 200 MiB for every browsing workload.

## Local production deployment

Revision `1.1.81-preview.20260920.3` was installed at 17:00 on September 20.
All twelve replaced application files were checked against the publish payload
by SHA-256, and the installed assembly was verified to have UI test mode disabled.
The main app was closed normally and relaunched; SearchHost remained running.
The previous binaries and window session are retained in ignored artifacts.

UI Automation then exercised the installed production process, without restarting
it between rounds. Each round opened four additional tabs alternating the user's
two directories, selected the original details tab, closed the four added tabs,
and sampled private working set after 15 seconds. The original four tabs and
selected directory were restored after each batch.

| Installed process, one tab (MiB) | Result |
| --- | ---: |
| Before browsing | 101.79 |
| Round 1 | 153.46 |
| Round 2 | 157.65 |
| Round 3 | 163.02 |
| Round 4 | 160.76 |
| Round 5 | 173.20 |
| Round 6 | 174.16 |
| Round 7 | 178.29 |
| Round 8 | 174.18 |
| Round 9 | 181.57 |

The final round fell from 190.51 MiB immediately after close to 181.57 MiB.
Earlier rounds reclaimed approximately 15–28 MiB after close. The later samples
fluctuate instead of rising every round, but nine rounds do not establish an
unbounded-growth guarantee or explain every remaining allocation. The warmed
process remains substantially larger than startup. Do not report this change as
eliminating all elevated memory usage. Raw measurements, installation hashes and
the before/after lifetime tests are retained under ignored `artifacts/`.
