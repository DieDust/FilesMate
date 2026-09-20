# FilesMate architecture

Implementation snapshot: 2026-09-20. Historical plans and ADRs describe intended designs; this page describes the current implementation.

## Toolchain

.NET SDK selection is pinned by global.json (10.0.103, latestFeature roll-forward, no prerelease SDKs). The UI targets net10.0-windows10.0.26100.0 with minimum Windows 11 22H2, build 22621. The installer is x64 only. The App references Windows App SDK component packages (WinUI 2.3.6, InteractiveExperiences 2.1.6, DWrite 2.1.0); it does not bundle the full Windows App SDK metapackage.

## Processes and dependencies

- FilesMate.App: WinUI 3 file manager, optional native window host for shell-selection compatibility, active folder sessions, indexing and previews.
- FilesMate.SearchHost: optional WPF tray/hotkey search UI. It uses the index maintained by the file manager and an application catalog. Residency and login startup are user preferences.
- A short-lived SearchHost invocation performs Office conversion. WebView2 creates its own browser processes on demand for document previews.
- FilesMate.Search: shared query contracts, settings and IPC.
- FilesMate.Core: framework-independent directory, scheduling, selection and operation contracts.
- FilesMate.Platform.Windows: filesystem, shell, thumbnail and native interop adapters.
- FilesMate.ShellHost: reserved stub, not used to isolate production file operations or shell extensions.

Closing a file-manager window releases its UI. Closing during tracked asynchronous file work waits for that work to finish. The optional search listener can remain resident according to the saved preference. Disabling background search stops that listener; it is not an always-on service.

## File and preview work

Directory sessions cancel obsolete enumeration and deliver batches to virtualized file surfaces. Viewport scheduling and bounded caches prioritize visible icons/thumbnails. Selection uses entry identity rather than relying on recycled controls. Folder and search previews load for explicit selection, release heavy content when closed, and ignore stale asynchronous results.

Pane paste/drop operations use the shared background FileShelfTransfer path. Recycle and permanent deletion use a short-lived STA worker; completion and undo bookkeeping return to the UI thread. A pane-operation gate prevents overlapping pane mutations. This is not yet a single durable operation queue for all entry points: shelf actions and undo/redo need further consolidation. Rename/create and undo/redo still include synchronous work.

The Windows adapter uses managed filesystem APIs and in-process IFileOperation callbacks for recycling, distinguishing recycled, permanently deleted and cancelled items. It is not an out-of-process RPC host. Managed deletion does not traverse directory links. Copying directory links is explicitly refused, and descendant destinations are checked after resolving directory aliases. Large operations can still complete partially on failure; there is no crash-safe transaction or resume journal.

Shared preview readers/renderers are used by the file manager and global search. Office conversion has format, size and timeout limits. Browser previews have restricted navigation, script and resource policies. Native decoders, COM shell extensions and Office workers are not an OS sandbox; see SECURITY.md.

## Persistent state

User profile data lives under the FilesMate local application data directory; UI-test builds redirect it to a test-profile under the build output. View settings can be per-folder (default) or global. Index roots default to ready local drives at depth 6; saved choices are preserved. First-run setup offers optional search/indexing and the bookmark bar. Bookmark groups are virtual containers, not filesystem directories.

## Performance evidence

Targets are in performance.md. Targets are not automatically achieved by the architecture. The dated audit records tested datasets and observed CPU/memory; startup and scrolling percentile gates still require controlled traces.
