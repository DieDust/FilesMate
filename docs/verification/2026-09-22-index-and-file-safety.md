# Index relocation and file-operation safety review

Scope: the missing index-size display, relocation of the live search index, concurrent manager/host settings writes, interrupted undo, cross-volume transfer, and installer process boundaries. All destructive experiments used generated fixtures; the user's real index and files were not used as migration or deletion fixtures. This follows the [1.1.85 review of the supplied report](2026-09-22-review-followup.md).

## Confirmed defects and changes

- **Missing size display:** the search settings page had item counters but no disk-usage control. It now shows database, journal and active rebuild temporary-file bytes, plus the last completed indexing time. File-stat reads run off the UI thread, at most one in flight, while the page is loaded.
- **Unsafe relocation ordering:** settings previously switched before moving the database. Moving separate SQLite files swallowed errors and accepted an existing destination. Relocation now creates a consistent SQLite backup, validates it, publishes a new file without overwriting an existing destination, and then atomically changes the configuration. Failure preserves the original. Normal unoccupied sources are removed using a verified file handle. A busy source or active WAL set is retained with a visible explanation and path.
- **Unrelated database modification:** loading index statistics previously executed schema creation against the configured path. Loading now validates the existing schema read-only. Rebuild refuses an unrelated database, reparse-point file, active sidecars, or a destination whose identity changed while scanning.
- **Concurrent configuration loss:** the manager and search host previously rewrote the same JSON independently. Both now use an exclusive cross-process lock, read the latest settings under that lock, update only intended fields, flush a unique temporary file and atomically publish it. Malformed configuration is not overwritten by a default object.
- **Wrong recycle version:** deleting another version at the same original path could make undo restore that later entry. Undo now uses the exact Windows recycle receipt and validates its identity. Large recycled directories do not require a full-tree scan to find their entry.
- **Interrupted undo state:** completed and unfinished items previously remained in one history entry after partial cancellation. The history now follows actual completed work, retaining its inverse operation separately and preserving the original validation state of unfinished work. This also covers a batch of replacements in which a later file is locked or externally modified; retained backups have one history owner.
- **Installer scope:** the old installer default could close unrelated applications that held payload files open. The installer now disables Restart Manager application closure by default; online upgrade passes the same explicit policy. FilesMate's own normal shutdown and path-checked SearchHost IPC remain in use. This finding does not establish the cause of the user's earlier QQ exit.
- **Cross-volume transfer:** a file move could silently enter an uncancellable system copy. Cross-volume moves now report byte progress and support cancellation, flush the full copy before deleting the exact verified source, and protect the published destination against concurrent writes. Replacement temporarily retains the old target even when undo was declined. If replacement succeeds but source deletion fails, the source, new target and old backup remain available; the error reports recovery locations.

## Why earlier tests missed these cases

1. The index-setting test saved and loaded a directory string. It did not move a real database, include committed WAL data, reject an occupied destination, or inject a failed configuration write.
2. The old recycle mock used a set of original paths, so it could not represent two deleted versions of the same path. Cancellation tests stopped before the first operation, leaving partial-completion history untested.
3. Installer smoke tests always supplied `/NOCLOSEAPPLICATIONS`; the actual online upgrade did not. The tested launch policy differed from the shipping one.
4. Source/UI structure assertions and a passing test count were insufficient evidence of disk-state correctness. This review checks original and destination bytes, identities, usable search results, configuration publication and history behavior after failures.
5. Same-volume transfer fixtures did not exercise the implicit cross-volume copy path. New tests use real C: to D: fixtures and inspect cancellation, bytes, identities, read-only attributes, alternate data streams and source-deletion failures.

## Verification evidence

- `artifacts/review-index-storage/index-storage.trx`: 20 isolated storage tests, including WAL, conflicts, cancellation and configuration-publication failures.
- `artifacts/index-config-harness/results/index-config.trx`: 21 configuration tests, including a separate process holding the lock and concurrent settings writers.
- `artifacts/github-public/artifacts/review-index-ui/index-storage-smoke.json`: actual WinUI settings page displayed 44 KB before and after relocation, retained its completion time and search results, and rejected a foreign target without changing either database or settings. A second live settings window followed the relocation. Injecting malformed JSON during a later relocation preserved the active custom index and its search results.
- `artifacts/review-update-boundary/result.json`: an unrelated test helper was enumerated as a file-lock holder by Restart Manager. No shutdown API was called.
- `artifacts/transfer-audit-harness/results/transfer-regression.trx`: 53 transfer tests passed, including 17 new cross-volume and failure cases.
- Final Release solution rebuild: 0 warnings, 0 errors (`artifacts/github-public/artifacts/review-186-final-build.log`).
- Final integrated tests: Core 149, App 790, Windows platform 202 and Integration 20; **1,161 passed, 0 failed, 0 skipped**. Results: `artifacts/github-public/artifacts/review-186-tests`.
- Local installation of **1.1.86** succeeded with installer exit code 0. All **1,519** installed payload files matched package hashes, and **17** settings JSON files retained their hashes. The installed manager and SearchHost were restarted from their registered installation path. Evidence: `artifacts/local-upgrade-1.1.86/install-verification.json` and `install.log`.
- Installer: `artifacts/github-public/artifacts/releases/FilesMate-Setup-1.1.86-win-x64.exe`, 85,144,633 bytes; SHA-256 `9B658DF2581A75C8CF9D1B7482DFC5615DCEF4B1D513DECCDCCBD9274C7E46C9`. This review installed locally; no public release was uploaded.

## Limits

This is a targeted regression and safety review, not proof that every filesystem operation is atomic against power failure or arbitrary concurrent external edits. No persistent multi-file transaction journal was added. A migration needs temporary space for a verified destination copy; an occupied old copy can remain and is reported. Cancellation of the SQLite backup is checked before publication and cannot cause deletion of an index whose configuration was already committed.

The disk-space preflight still resolves space by drive root and does not accurately describe every mounted-volume arrangement. Actual copy failure preserves source data. Native replacement rejects a read-only source or target without clearing its protection.

The earlier review's remaining engineering work is still explicit: no complete persistent crash-recovery journal or path-level operation scheduler (F05), no low-privilege Office sandbox or complete malicious-document corpus (F09), and no new Authenticode signing, full SBOM or fixed-hardware performance pipeline (F12). Metadata identity checks do not prove full content integrity. Real local-drive transfer tests do not establish coverage of every network/removable-device failure combination. High-frequency short search terms can still sort many matches; the earlier million-row `r` benchmark had a P95 of about 767 ms.

The implementation uses the documented [Microsoft.Data.Sqlite backup API](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup) to preserve committed [SQLite WAL state](https://www.sqlite.org/wal.html). Installer behavior was checked against [Inno Setup's CloseApplications documentation](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm).
