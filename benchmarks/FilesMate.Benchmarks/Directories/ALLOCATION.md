# Enumeration allocation notes

`WindowsDirectoryEnumerator` converts each `WIN32_FIND_DATAW` once into `FileEntryCore`.

Per published entry the retained managed payload is:

- one name `string` (no duplicated full path)
- the `FileEntryCore` value itself (copied into a batch array)

There is no per-entry dictionary, ViewModel, `Task`, or extra handle call for file IDs.

Expected order of magnitude on the reference machine (Release, name ~12 characters):

| Dataset | Entries | Dominant retained cost | Notes |
| --- | ---: | --- | --- |
| 1k | 1,000 | ~ tens of KB of name strings + batch arrays | First batch should land well under the 200 ms warm-rows gate |
| 10k | 10,000 | ~ hundreds of KB | Sort/filter later; enumeration stays streamed |
| 100k | 100,000 | low-single-digit MB of names | UI must render a viewport before the last batch |

`Directory.EnumerateFileSystemEntries` is a comparison-only baseline in `DirectoryEnumerationBenchmarks`. It is not a production fallback.

Measure with:

```text
dotnet run -c Release --project benchmarks/FilesMate.Benchmarks -- --filter *DirectoryEnumeration*
```
