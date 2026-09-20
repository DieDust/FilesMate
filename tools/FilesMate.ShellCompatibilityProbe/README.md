# Native Shell selection compatibility investigation

This is a **developer feasibility fixture**, not a shipped SnowShot integration.
Neither executable is included in the FilesMate installer.

`probe.cpp` diagnoses the API chain used in SnowShot's original
[`selectedfiles.cpp`](https://github.com/mg-chao/snow-apps/blob/31b24fb55232819caf2901c014ec1c424ab53d45/snow_shot/src/platform/windows/selectedfiles.cpp).
It records window classes, HRESULTs, and counts, without logging file paths or titles.
It deliberately continues after a failed window-class gate to diagnose COM separately;
the original SnowShot returns immediately at that gate. Desktop handling, cancellation,
and real foreground capture are not tested by this diagnostic.

`host.cpp` creates an independent Win32 process with its own `CabinetWClass` window,
hosts a genuine Shell view, registers its `IWebBrowserApp` in `IShellWindows`, and
exposes `IServiceProvider -> IShellBrowser -> IShellView -> IFolderView2`.
The view is shown off-screen without activation and the host exits after 20 seconds.
Its COM stubs are only for this limited experiment, not production implementations.
It uses standard Windows interfaces and does not patch, inject into, launch, configure,
or load any SnowShot binary. It does not copy to the clipboard or intercept hotkeys.

## Build

In a Visual Studio x64 developer command prompt, from the repository root:

```bat
cl /nologo /std:c++20 /EHsc /W4 /DUNICODE /D_UNICODE tools\FilesMate.ShellCompatibilityProbe\probe.cpp /Foartifacts\shell-compatibility-probe\probe.obj /Feartifacts\shell-compatibility-probe\probe.exe /link ole32.lib oleaut32.lib shell32.lib user32.lib uuid.lib
cl /nologo /std:c++20 /EHsc /W4 /DUNICODE /D_UNICODE tools\FilesMate.ShellCompatibilityProbe\host.cpp /Foartifacts\shell-compatibility-probe\host.obj /Feartifacts\shell-compatibility-probe\host.exe /link ole32.lib oleaut32.lib shell32.lib user32.lib uuid.lib
```

Create the artifact directory first. Start `host.exe <absolute-fixture-folder> <filename>`
as a hidden helper with stdout redirected, wait until it prints `select=0x0`, then run
`probe.exe --expected <absolute-fixture-file>`. The client must report
`snowshot_capture=PASS`, `captured_view_matches=PASS`, and `expected_selection=PASS`.
Exit code 3 means the expected selection was not obtained; code 0 means it was.
For diagnostics against an existing window, use `probe.exe <decimal-HWND>`.

## What success does and does not establish

Success establishes that a third-party process can implement the window shape and
native COM path SnowShot expects, without an Explorer process-name requirement.
It **does not** establish that the installed SnowShot build accepts FilesMate today,
or that FilesMate's WinUI 3 and WPF windows have been migrated to this host model.
That migration must preserve activation, IME, resizing, DPI, accessibility, themes,
drag/drop, tab lifetime, and existing incoming `SHOpenFolderAndSelectItems` behavior.
