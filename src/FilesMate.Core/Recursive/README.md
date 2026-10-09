# Recursive browsing model

This is an independent, manually started tree session. It does not alter ordinary
`DirectorySession` or the name-keyed update/reconciliation rules of `EntryStore`.
The application must first gate the root to a normal local fixed/removable-drive
folder and must keep the feature off by default.

## Identity and publication

- `(SessionId, node Id)` identifies a row. Ids are 1-based and append-only within
  one session. ParentId 0 is the virtual root. Direct children have Depth 0
- Entry Id and node Id agree; entry source position is Id minus one. Repeated
  filenames in different directories remain separate. Display names never serve
  as full-path identity
- `RecursiveSnapshot` captures entry blocks and compact parent-node blocks at one
  monotonic Revision. Paths are reconstructed from those captured blocks. Do not
  combine a projection's index with another snapshot's resolver
- The `EntryStore` exposed by the projection exists solely for the existing
  virtualized UI adapter. Only the session may append to it. Never call Rename,
  Reconcile, Upsert, RemoveByName, or other mutation paths on that adapter
- Progressive and cancelled indexes allow unchanged append-only prefixes. Final
  indexes require exact freshness. The cancelled snapshot retains the last
  coherent publication, so entries collected since it may not appear

## Traversal and lifecycle

- RunAsync is idempotent and always puts enumeration on a background worker.
  Enumeration is serial by directory with a bounded provider batch request. There
  is no per-file task fan-out; the directory-ID queue is bounded by MaximumNodes
- Defaults are 200,000 nodes, 256 entry levels beneath the root, and 200 ms progress
  coalescing. A reached budget is an explicit Partial result with Limits flags
- Directory reparse points and offline directories remain visible but are not
  traversed. The default probe checks the path's ancestors from the volume root
  outward before touching the target and rechecks immediately before enumeration
  to catch replacement since the parent listing. This is an advisory filesystem check,
  not a handle-pinning security boundary against an adversarial concurrent rename
- Malformed/repeated child entries and inaccessible/disappearing branches are
  skipped and counted. Root failure fails the session. A provider stream without
  a final marker cannot claim successful completion
- Ambiguous Windows components (trailing dot/space, reserved devices, or invalid
  Win32 characters) are excluded explicitly, because reconstructing a normal
  operation path must not redirect an extended-API name to a different target
- Cancel synchronously closes the mutation/publication gate. Late provider data
  is rejected. Provider cleanup may finish later if a native call is blocked.
  DisposeAsync waits for cleanup; repeated disposal shares the same cleanup task
- Changed runs on the worker. Subscribers must post to their UI dispatcher and
  return promptly. Subscriber exceptions are isolated in NotificationError
- No recursive watcher, content search, thumbnails, hashing, shell properties,
  shortcut targets, hard-link counts, or directory-size calculation starts here.
  The application handles invalidation and explicit refresh by replacing the
  session; it must never replay ordinary name-based watcher deltas into this model

## Projection and operations

- BuildProjection is CPU-only background work. It preserves hierarchy and sorts
  siblings with existing natural/pinyin ordering. Location has equal keys within
  a sibling group, with name as its tie-breaker; FullPath within a sibling group
  compares leaf names because the parent prefix is identical
- A non-matching ancestor needed to reach a matching descendant is context-only.
  It is excluded from MatchCount and IsSelectable. Active filters temporarily
  ignore collapsed state so descendants remain discoverable; clearing the filter
  restores the captured collapse state
- Collapse only affects the flat visible integer index, never scan scope
- ResolveOperationPaths validates selection against this exact projection,
  rejects invisible/context-only rows, deduplicates IDs, and removes descendants
  of a selected directory by parent identity. It is linear in selected IDs times
  bounded depth, without repeated linear IndexOfId searches or filesystem I/O
- A selected directory still represents its whole filesystem subtree. Operations
  must disclose consequences for hidden/unmatched descendants and revalidate
  filesystem state through their existing operation planner before execution

## Conservative legacy-operation compatibility

- Case-distinct siblings remain separate rows and IDs. A same-parent ordinally
  distinct but OrdinalIgnoreCase-equal pair marks both identities as risky; their
  descendants inherit that risk. Nothing is silently merged or dropped
- `EvaluateOperationSafety` checks every distinct selected ID before legacy path
  planners or ancestry deduplication run. It returns explicit flags and counts;
  unsafe/invisible/context-only IDs reject the selection rather than reducing it
- IdentityOnly is for tags and other non-recursive identity consumers. It requires
  complete containing/ancestor listings and no observed alias along the identity
  chain. Subtree additionally rejects any selected directory containing colliding
  identities, pending/unscanned/skipped/failed directories, or omitted entries
- Root listing completion is explicitly captured separately from overall scan
  state. A complete unrelated branch may pass while another branch still scans.
  A previously published incomplete snapshot remains conservative after later data
- The scan must include hidden and system entries for complete identity coverage.
  BuildProjection's excludedAttributes hides such rows and all descendants of a
  hidden directory, while keeping their metadata in operation risk evaluation.
  Attribute preferences do not auto-expand collapsed branches. Limits count all
  scanned metadata, including entries suppressed by visibility preferences
- App may require a native `IRecursiveOperationCompatibilityProbe`. Each directory
  captures NotRequired/Pending/Compatible/Unverified; missing, false or failed
  required probes retain a readable tree but block legacy operations on that
  identity, descendants, and selected covering ancestors. No native capability is
  inferred from spelling. Pure model callers default to verification not required
- Two byte arrays and forward/reverse linear passes precompute projection risk;
  safety evaluation is linear in selected IDs. Exact-path open and Copy Path are
  separate read-only actions and need not use this mutation-compatibility guard

These checks are bounded observations, not a claim of support for every NTFS
case-sensitive behavior or a substitute for action-time filesystem revalidation.

The synthetic benchmark under `benchmarks/FilesMate.RecursiveHarness` measures
this pure model only. It does not establish Windows disk-scan or GUI acceptance.
