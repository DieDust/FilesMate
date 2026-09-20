# FilesMate performance

Planned budgets from the implementation plan; measured compliance is not yet established for all scenarios. If convenience conflicts with a hard performance rule, the performance rule wins unless an ADR raises the gate.

All measurements must record hardware, OS build, FilesMate commit, build configuration, dataset, median, P95, and P99 where applicable.

## Budgets

| Metric | Target | Hard gate | Notes |
| --- | ---: | ---: | --- |
| Warm launch to interactive window | <= 150 ms | <= 250 ms | Release x64; no restored slow network tab |
| Cold launch to interactive window | <= 300 ms | <= 500 ms | Reference NVMe machine |
| Local folder first meaningful rows, warm cache | <= 100 ms | <= 200 ms | At least one viewport of names |
| Local folder first meaningful rows, cold cache | <= 250 ms | <= 400 ms | Excludes media thumbnail completion |
| 100,000-entry folder first meaningful rows | <= 300 ms | <= 500 ms | UI must remain interactive while enumeration continues |
| UI-thread longest task during normal browsing | <= 16 ms | <= 50 ms | Any >50 ms event is a defect |
| 60 Hz scroll frame time | P95 <= 16.7 ms | P99 <= 33 ms | 100,000-entry details and grid datasets |
| Single normal tab idle working set | <= 100 MB | <= 150 MB | After 30 seconds idle, thumbnails disabled |
| Ten restored tabs idle working set | <= 160 MB | <= 220 MB | Only active tab eagerly loaded |
| In-memory decoded thumbnail cache | <= 64 MB | <= 80 MB | Weighted by decoded bytes, not file bytes |
| In-memory icon cache | <= 16 MB | <= 24 MB | Weighted LRU |
| In-memory directory snapshots | <= 32 MB | <= 48 MB | Weighted LRU across tabs/history |
| On-disk thumbnail cache | <= 512 MB | <= 600 MB | Incremental LRU eviction; never clear everything at once |
| Idle CPU | median 0.0% | P95 <= 0.3% | 60-second window, no active operation |
| Idle filesystem/network reads | 0 continuous reads | No periodic scans | Event-based watcher is allowed |
| Exit time | <= 500 ms | <= 1 second | Optional search residency is measured separately |

## Budget change procedure

1. Capture a reproducible failing trace.
2. Prove the issue is not a debug build, test harness, antivirus transient, or dataset error.
3. Attempt at least one implementation improvement.
4. Write an ADR with before/after numbers and user-visible trade-off.
5. Obtain user approval before raising a hard gate.

## Measurement

Use `scripts/create-perf-data.ps1`, `scripts/run-perf.ps1`, and `scripts/capture-etw.ps1` for datasets and trace preparation. Dataset definitions live in `docs/performance-datasets.md`.

Current tooling caveat: `run-perf.ps1` records environment and dataset presence
only; it does not yet measure or certify the budgets above. For observational
idle CPU and memory samples of one running instance, use
`pwsh ./scripts/measure-idle.ps1 -Seconds 60`. Add `-ProcessId <pid>` to select
an isolated app or the search host. This does not measure startup,
filesystem reads, or frame times. Record the measured scope alongside any results.

Profile builds (`pwsh ./scripts/build.ps1 -Configuration Profile`) are optimized with portable PDBs so ETW/WPA stacks remain usable.

## Enumeration allocations

The production enumerator converts each Win32 find record once into `FileEntryCore`. It stores the entry name only; the directory root stays on the request/session. There is no per-entry full path, dictionary, ViewModel, or `Task`. See `benchmarks/FilesMate.Benchmarks/Directories/ALLOCATION.md`.

## Forbidden performance anti-patterns

- Polling the filesystem on a timer.
- `ObservableCollection.Add` once per directory item.
- One ViewModel per file.
- Unlimited dictionary caches.
- Full-path strings duplicated in every cache layer.
- Generating thumbnails for off-screen items merely because they are in the directory.
- Continuing work after navigation because cancellation is treated as optional.
- Calling `.Result`, `.Wait()`, synchronous COM, or filesystem enumeration on the UI thread.

## Verification scope

See [public release checks](public-release.md). Functional tests do not certify the budgets above. The PerformanceTests project contains an explicitly skipped placeholder. Publish new benchmarks only with reproducible workloads and measurements that include child processes.
