# Undo backup policy (1.1.79)

The previous 32-entry session history had no byte limit. Replacing large files could retain a large amount of data in hidden sibling folders without explaining the cost to the user.

## Limits and lifetime

- Retain at most **5 operations** across the session's undo/redo history. Discarding an entry releases its replacement versions.
- Replacement backups reserve **256 MiB total**, at most **64 MiB per file** and **256 files**. The shared profile ledger includes registered remnants from earlier processes; restarting does not reset the budget.
- Reserve the larger of the incoming and existing versions, including alternate data streams. This covers a copy replacement's undo/redo swap, when the new version becomes the retained version.
- These are logical reserved bytes, not filesystem allocated-space measurements. Compression, sparse files, allocation units and external edits can change actual disk usage. The app never reads entire files just to measure their size.
- Normal closure of the last app window clears live history. A new operation clears abandoned redo history. There is no periodic full-drive scan or timed file deletion.
- Ordinary copy/move/rename history records paths; it does not make another full copy of each source merely to support Undo. Incoming copy staging is separate from retained replacement backups and still needs free space.

## Replacement interaction

The conflict dialog explains reserved space and the Settings → Files & folders management entry. If the per-file/total/count limit is exceeded, or safe backup reservation is unavailable, users can skip, keep both, cancel to manage history, or choose **Replace without undo**.

The irreversible choice requires a separate confirmation, defaults to Cancel and cannot apply automatically to later conflicts. A remembered protected Replace decision is interrupted when the budget becomes unavailable. Nothing is silently overwritten without a backup under the protected Replace choice.

An irreversible replacement invalidates the whole batch's undo and previous undo/redo history after a replacement actually succeeds. The confirmation states this explicitly. This prevents an earlier copy/rename undo record from deleting or moving a file subsequently replaced without undo. Cancelled or failed attempts do not clear history.

Before staging, replacement checks available destination-drive space, including incoming data for copies/cross-drive moves and a 128 MiB reserve. This is a preflight check, not a disk-space guarantee: other applications can consume space concurrently. Filesystem failures preserve recoverable data and are reported; the old file is not truncated to free space.

## Visibility, crashes and cleanup

Settings shows reserved bytes, registered backup locations, and **Clear current undo history**. Clearing does not remove the current source/destination files. The UI explains that clearing removes the ability to Undo/Redo. It does not offer cleanup during a live file operation.

Each reservation is journaled before replacing the destination. Registration uses a flushed temporary journal followed by a rename, under an exclusive cross-process gate. Startup reloads only these small records, not user folder trees. Registered remaining locations trigger a visible management notice and continue to consume budget.

If the journal cannot be created/read, protected backup creation is unavailable; it does not fall back to untracked in-memory accounting. An offline drive is not treated as proof of deletion.

Crash remnants and backups changed externally are retained for inspection/recovery, with buttons to open their locations. They are not blindly deleted by folder-name prefix. Users can remove them after recovery; missing locations are pruned from the ledger. Unregistered leftovers created by older versions are not automatically discovered or deleted. A crash during incoming-file staging may also leave a temporary incoming copy; that is not a registered replacement backup.

Stream sizes use the Windows [FindFirstStreamW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirststreamw) / FindNextStreamW API and [WIN32_FIND_STREAM_DATA](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-win32_find_stream_data). Filesystems that do not support named-stream enumeration use the ordinary file length.

## Verification

Tests cover shared ledger reload, per-file/total limits, interrupted bulk Replace rules, irreversible copy/move, mixed batches, alternate streams, safe history clearing and cancellation of the second confirmation. Existing replacement undo/redo, rename, conflict and partial-failure tests remain part of the regression suite. UI smoke uses an isolated profile and disposable files, not user documents.

Local verification on 2026-09-19: Core 125, Windows platform 161 and App 721 tests passed (1,007 total). All 24 convenience UI checks passed, including the two new backup-management/confirmation checks. Evidence: `artifacts/backup-{core,platform,app}-tests.log`, `artifacts/backup-ui-smoke.json`, `artifacts/backup-management-1.1.79.png` and `artifacts/backup-no-undo-1.1.79.png`.

Installed `1.1.79-preview.20260919` locally and reopened the installed application. Installer exit code 0; all 1,519 payload files matched the package hashes and all 17 existing profile JSON files were preserved. Evidence: `artifacts/upgrade-1.1.79/verification.json`. The public update feed was not changed.
