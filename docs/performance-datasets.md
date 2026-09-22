# FilesMate performance datasets

Generated trees are local measurement fixtures. They are never committed.

## Marker

Every generated root contains `.filesmate-dataset-marker` (JSON). Cleanup refuses a directory without that marker. The marker identifies `FilesMate.TestDataGenerator` version 1 plus seed, profile, and counts.

## Generator CLI

```text
dotnet run --project tools/FilesMate.TestDataGenerator -- create --root <absolute-path> --files <n> --directories <n> --depth <n> --seed <n> --profile empty|mixed|images|unicode|long-paths
dotnet run --project tools/FilesMate.TestDataGenerator -- clean --root <absolute-path> --require-marker
```

Safety:

- Absolute paths only.
- Drive roots, the FilesMate workspace root, ancestors of the workspace, and the user profile root are rejected.
- Cleanup walks the tree without following reparse points.
- Long names are written with `\\?\` prefixes so MAX_PATH does not truncate them.

## Canonical datasets

Created by `scripts/create-perf-data.ps1 -Root <absolute-path>`:

| Folder | Profile | Files | Directories | Depth | Seed | Notes |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `small-1k` | mixed | 1,000 | 40 | 3 | 1 | Warm-up listing |
| `medium-10k` | mixed | 10,000 | 80 | 3 | 2 | Sort/filter |
| `large-100k` | mixed | 100,000 | 120 | 4 | 3 | Virtualization / first-rows gate |
| `images-10k` | images | 10,000 | 30 | 2 | 4 | Tiny valid PNG/JPEG |
| `unicode-5k` | unicode | 5,000 | 40 | 3 | 5 | CJK, emoji, RTL, combining marks, reserved-looking, long names |
| `deep-tree` | mixed | 200 | 40 | 20 | 6 | One spine of 20 levels; extra dirs fan out under the cap of 40 |

`deep-tree` does not create 10 children at every level (that would explode). It creates at most 40 directories with maximum depth 20, plus 200 files. Documented total payload is 240 filesystem entries plus the marker.

## Scripts

- `scripts/create-perf-data.ps1` builds the generator and materializes the table above.
- `scripts/run-perf.ps1 -Scenario ResourceChecks` uses small self-owned fixtures and writes measured JSON, Markdown and TRX under `artifacts/perf/<commit>/<timestamp>/`; it does not consume the canonical UI datasets above.
- `scripts/capture-etw.ps1` starts WPR, runs an optional script block, and always stops the session in `finally`.
