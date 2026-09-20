# Contributing to FilesMate

Use Windows 11 22H2 or later, x64, PowerShell 7, the .NET SDK selected by global.json, and the WinUI build dependencies referenced by the projects. Inno Setup 6 is required only for packaging.

## Development

1. Create a branch for a focused change.
2. Run `pwsh ./scripts/build.ps1 -Configuration Release` and `pwsh ./scripts/test.ps1 -Configuration Release`.
3. For UI work, check light/dark themes, keyboard focus, scaling, selection and window resizing. Use an isolated `FilesMateUITest=true` build so tests do not change personal settings.
4. Describe the user-visible behavior and the tests actually performed. Include measurements for performance claims.

Do not run the build scripts over a directory that contains the running installed application. Use an isolated output directory or close that installation first. File-operation tests must use disposable, clearly marked fixtures, never personal documents. Keep screenshots, profiles, indexes, dumps, installers and generated test data under ignored artifacts directories.

## Design and safety

Use shared theme and motion resources. Keep directory enumeration, decoding and large file operations off the UI thread; bound caches and reject results from obsolete requests. Never follow directory links recursively during destructive operations. A cancelled operation must not be retried by a fallback implementation.

Preserve existing settings and bookmark data during upgrades. New background behavior needs an explicit preference and must respect the first-run choices. Default index depth remains 6.

## Translations

English, Simplified Chinese and Japanese share one resource table across the file manager and search host. Follow [docs/localization.md](docs/localization.md) for stable keys, formatting, the terminology glossary and resource generation. Check translated screens at narrow widths as well as light/dark themes; include screenshots for wording or layout changes.

## Licensing

Contributions are provided under Apache-2.0. Retain existing third-party headers. New dependencies and copied assets must include their provenance, exact version, license and required notices. Update THIRD-PARTY-NOTICES.md and installer/ThirdPartyNotices when appropriate. Do not commit credentials or personal file paths in test fixtures.
