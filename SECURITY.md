# Security

FilesMate is currently a preview. Security fixes target the latest preview; there is no supported long-term maintenance branch yet.

## Reporting

Use [GitHub private vulnerability reporting](https://github.com/DieDust/FilesMate/security/advisories/new) (Security → Report a vulnerability). Include the affected version, impact and minimal reproduction using synthetic files. Do not put credentials, real personal documents, profile databases or crash dumps in public issues.

## Trust boundaries

- FilesMate runs with the current user's filesystem permissions. It is not a privileged service. Opening an application, shortcut or executable is an explicit user action and can execute that file.
- Markdown and generated Office previews disable page scripts and web messages. The bundled PDF.js viewer enables its trusted local scripts and a validated close-preview message; PDF document JavaScript/eval is disabled. All preview modes deny permission grants, downloads and new browser windows. Markdown is rendered with raw HTML disabled and a restrictive content security policy. Explicit web links may open in the default browser; local Markdown image references use a virtual mapping of the document directory.
- Managed Office conversion runs in a separate process with input/output limits, a 15-second timeout, and a Windows job limiting committed process memory to 384 MiB and membership to one process. The job is assigned before the worker runs and closes with its owner. This is resource containment, **not a restricted-token or AppContainer security sandbox**. Native installed Office preview handlers use a separate watchdog-controlled host; they do not share this managed-converter quota. Third-party parsers and the installed WebView2 Runtime remain part of the attack surface.
- Native shell context menus, thumbnails and compatibility interfaces run with the current user. Third-party shell extensions are not isolated in a restricted process. The reserved ShellHost project does not provide such isolation.
- Directory copy rejects loops/descendant destinations and refuses directory symbolic links. Managed deletion removes a directory link itself, rather than traversing its target. These checks are not a complete defense against another process racing filesystem changes.
- Settings, bookmarks, search indexes and caches are local profile data. Filenames, paths and document-derived content may appear in those files and diagnostic logs. They are not encrypted by FilesMate; Windows account and disk protections apply.

Before a public release, rerun dependency advisories and secret scanning, review third-party notices, verify installer hashes and run the file-operation/preview tests. See [public release checks](docs/public-release.md) for measured coverage and remaining limitations.
