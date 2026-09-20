# FilesMate testing

Tests exist to lock contracts, cancellation, and resource bounds. Compilation is not completion.

## Layout

| Project | Role |
| --- | --- |
| `tests/FilesMate.Core.Tests` | Contracts, sessions, caches, sort/filter, scheduling |
| `tests/FilesMate.Platform.Windows.Tests` | Path normalization, Win32 enumeration, watchers, icons |
| `tests/FilesMate.IntegrationTests` | Real filesystem behavior inside marked temp roots |
| `tests/FilesMate.App.Tests` | Navigation and virtualized surface behavior with fakes |
| `tests/FilesMate.PerformanceTests` | Release-gate scenarios; not part of the default test script |
| `benchmarks/FilesMate.Benchmarks` | Microbenchmarks (BenchmarkDotNet) |
| `tools/FilesMate.TestDataGenerator` | Deterministic datasets |

## Commands

```powershell
pwsh ./scripts/test.ps1 -Configuration Release
```

`scripts/test.ps1` builds once, then runs every non-performance test project. TRX logs and coverage outputs go to `artifacts/test-results`.

Narrow filters used by the plan:

```powershell
dotnet test tests/FilesMate.Core.Tests/FilesMate.Core.Tests.csproj -c Release --filter FullyQualifiedName~Contracts
dotnet test tests/FilesMate.Platform.Windows.Tests/FilesMate.Platform.Windows.Tests.csproj -c Release --filter FullyQualifiedName~Directories
```

## Safety

Destructive integration tests run only inside generator-marked temporary roots. Cleanup must refuse drive roots, the workspace root, the user profile root, relative paths, and unmarked directories. Tests must never follow reparse points while generating or cleaning.

UI tests use fake enumerators. They must not require the developer's real filesystem.

## Protocol for production work

For every feature task:

1. Add or update a test that initially fails.
2. Run the narrow test and record the expected failure.
3. Implement the smallest production change that makes it pass.
4. Re-run the narrow test and the relevant project suite.
5. Run a performance check when the task touches enumeration, rendering, caches, metadata, startup, or operations.

A feature is done only when cancellation, stale-result rejection, bounded concurrency, and the relevant performance scenario are covered. See the definition of done in the implementation plan.
