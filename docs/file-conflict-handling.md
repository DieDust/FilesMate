# File name conflicts

Version 1.1.77 uses the same conflict decision and transfer engine for paste, drag and drop, dual-pane transfers, the file shelf, and conflicting renames.

| Conflict | Choices |
| --- | --- |
| Two ordinary folders | Merge, Skip, Keep both, or Cancel |
| Two distinct ordinary files | Replace, Skip, Keep both, or Cancel |
| Different item types or linked destinations | Skip, Keep both, or Cancel |
| Copying an item into its own folder | Skip, Keep both, or Cancel; never merge a folder with itself |
| Cutting an item into its existing parent | Leave it unchanged |

Keep both allocates an available parenthesized number before a file extension, for example `report (2).pdf`. Dots in folder names are retained as part of the folder name: `v1.2 (2)`. The dialog previews the available name; it is checked again when the operation runs.

Merge combines folder contents. It does not combine arbitrary file contents or authorize file replacement. Each conflicting child is processed through the same decision flow. “Apply to remaining conflicts of this kind” lasts for this operation only, with separate choices for mergeable folders, replaceable files, and other conflicts. Applying folder merge to all does not silently skip or overwrite matching files. Replacing a folder wholesale is deliberately not offered.

## Comparison and replacement

The design follows the basic decisions documented by [Directory Opus](https://www.gpsoft.com.au/help/opus12/Documents/The_Replace_Dialog.htm) and [FreeCommander](https://freecommander.com/fchelpxe/en/Copy.html): replace, skip, and keep both, with enough file information to make the choice. FilesMate displays side-by-side paths (full path on hover), exact byte sizes, local modification times including seconds, and a newer-time indicator. Matching dates and sizes are not represented as proof of matching contents. English, Chinese and Japanese resources are provided. The initial selection and primary button both say Skip; Enter performs that non-destructive action. Selecting another action updates the button and any relevant explanation.

The 1.1.77 layout displays the filename once, uses compact comparison text and grouped choices, and collapses the empty explanation rather than reserving blank space. Copying an item onto itself (including a directory alias for the same file) has a dedicated explanation, a single path, and Create a copy / Skip choices. It does not offer self-replacement. Link and file/folder type conflicts also explain why replacement is unavailable. Same-item batch choices are scoped separately from other non-replaceable conflicts.

Replace prepares a sibling staging file before calling `File.Replace`, with a previous-version backup in an exclusively created hidden `.filesmate-history-<guid>` directory on the destination volume. Copy uses the Windows file-copy implementation; move stages the source via `File.Move` (including cross-volume moves). Cancellation and failure before publication restore a staged move to its original name without overwriting an intervening file. There is no destructive overwrite fallback when replacement is unsupported or denied. Read-only destination protection is not removed automatically.

Comparison metadata and Windows file identity are checked again before committing; changes while the dialog is open discard the cached choice and ask again. Read handles deny concurrent writers during preparation. Identity checks also exclude replacing an item with itself through another hard-link or junction path. Other processes can still rename paths concurrently, and this is not a transaction across a whole batch or a protection against a hostile process changing filesystem paths at precisely the same time.

Previous versions are attached to the same undo entry as the rest of the transfer. Replacement undo/redo preserves both versions; a moved incoming file returns to its source on undo. Metadata checks refuse to overwrite subsequent edits or a newly occupied source name. These checks use size, times and attributes, not full-file hashing. Clearing redo, evicting entries beyond the 32-operation history limit, and closing the last application window release owned backups. Backup deletion is non-recursive. Inaccessible or externally modified backups are retained. An abnormal process termination can also leave a hidden history directory for manual recovery; the initial backup retains the old filename inside it (undo/redo may rotate the backup name). Undo history does not survive application exit.

Skip preserves the source and destination. Cancel stops remaining work and retains undo information for work already completed. A partial or skipped cut does not report full clipboard completion or clear the cut clipboard. Copy undo tracks new files and directories separately so it cannot recycle a pre-existing merge destination; empty-directory cleanup is non-recursive and preserves later-added files.

Copies use a reserved sibling staging file and Windows file copy to retain timestamps and alternate streams, then publish with a non-overwriting move. Cancellation is observed between items and before publication of a staged file; an OS file copy already in progress finishes before that cancellation check. Directory links are not traversed. Filesystem mutations by other processes can still cause an operation to fail; name availability checks are never authorization to overwrite.

Windows distinguishes collision renaming and extension preservation in its [file-operation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags). FilesMate exposes the decision first and records only its own completed changes for undo, rather than enabling automatic renaming unconditionally.

Validation: 714 App tests, 145 Windows platform tests and 118 Core tests passed. All 18 UI smoke checks passed, including the replacement comparison dialog and paste/undo/redo. New real-file cases cover copy and move replacement, cross-volume move, combined folder merges, same-named incoming files, repeated undo/redo, locked/read-only targets, changed comparisons, source-name collisions, cancellation, batch rule separation, and backup release on history eviction. Generated evidence is retained locally in `artifacts/replace-*-tests.log` and `artifacts/replace-ui-smoke.json`.

For the 1.1.77 layout and self-copy clarification, 714 App tests and 145 Windows platform tests passed again. All 19 UI smoke checks passed, including explicit same-item messaging, one displayed path, compact content height, default Skip, numbered self-copy, and ordinary file replacement with undo/redo. The final radio and checkbox labels were visually checked for spacing and vertical alignment. Evidence: `artifacts/compact-conflict-*-tests.log`, `artifacts/compact-conflict-ui-smoke.json`, `artifacts/compact-conflict-replace.png`, and `artifacts/compact-conflict-self.png`.

The 1.1.76 installer completed locally with exit code 0; 1,519 payload file hashes matched and all 17 JSON settings files were unchanged. The installed application was reopened. Evidence is recorded in `artifacts/upgrade-1.1.76/verification.json`.

The 1.1.77 local installer likewise exited with code 0, verified all 1,519 payload files, preserved all 17 JSON settings files, and reopened the installed application. Evidence: `artifacts/upgrade-1.1.77/verification.json`.
