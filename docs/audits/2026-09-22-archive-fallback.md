# Optional CompactMate and built-in ZIP

Version: 1.1.88.

The file manager and file shelf now share archive routing. An installed CompactMate remains the preferred provider; absence selects the built-in ZIP implementation. A launch error from a detected CompactMate is surfaced without executing the operation again. Search-result actions keep their existing handoff to the file manager. Both menus asynchronously disable external-only actions when CompactMate is absent.

The built-in implementation stages output on the destination drive, validates before publishing, and reuses existing conflict handling and backup budgets. Staging moves become creation/copy undo entries, retaining the same replacement backup lease. Multiple archives that replace files inside a newly published folder have repeatable undo/redo records without including internal backups in a recycled folder snapshot. Transfer errors retain recovery paths.

## Verification

- Full Release solution build: 0 warnings, 0 errors.
- Core tests: 151 passed; App tests: 866 passed; Windows platform tests: 281 passed; integration tests: 20 passed. Total: **1,318 passed**, none skipped.
- ZIP-specific isolated harness: 64 cases, including CRC, ZIP64 bounds, path traversal, collisions, real junction/hard-link checks, cancellation, file timestamps and Internet zone propagation.
- Archive transfer harness: 23 cases, including production Windows operations, replacement undo/redo, backup budgets, multi-archive merges, partial cancellation/failure, preserved recovery messages and cleanup boundaries.
- Routing, localization and search-handoff harness: 48 cases.
- Real WinUI exercise: create/extract a Chinese-named ZIP, open the replace dialog between progress dialogs, click the progress Cancel button on a 32 MiB input, verify destination results and temporary cleanup.

One local synthetic 32 MiB compression/extraction run took 973 ms, with 874,768 bytes of cumulative managed allocation and 11 progress notifications. This measures that fixture, not resident memory or general throughput. Payloads use a 64 KiB buffer; per-file forced disk flushes are avoided.

## Boundaries

Scope and limits are documented in [archive support](../archive-support.md). External CompactMate internals are outside these tests. The selected directory tree is not a transactional filesystem snapshot. Unhandled process termination and power loss can leave staging data. This release does not auto-delete those directories at startup.

Windows metadata-only directory handles did not prevent rename in the native fixture. Real directory-read handles pin names, while private open marker files keep owned staging directories nonempty against empty-directory reparse conversion. Preparation and publication use compatible sharing modes. These are file-operation controls, not a sandbox against arbitrary malicious code with the user's privileges. Source directories receive no markers.

The system folder picker may remain open until dismissed after cancellation; the cancellation check prevents subsequent writes. Actual native replacement rollback failure and power loss were not reproduced. Error-propagation injection confirms that manual recovery locations remain visible.

References: Microsoft's [.NET ZIP/TAR guidance](https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices) and [ZIP CRC API](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchiveentry.crc32?view=net-10.0).
