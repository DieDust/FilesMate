# ADR 0001: Managed-first with native hotspot fallback

- Status: Accepted
- Date: 2026-08-29
- Decision makers: Product owner (plan baseline)

## Context

FilesMate must replace File Explorer for normal daily work while staying visibly responsive on 100,000-entry folders and remaining inactive after exit. File Pilot shows that a custom renderer and low-abstraction hot path can meet that bar. A full custom UI engine in version 1 would delay a usable daily driver.

WinUI 3 plus .NET 10 can deliver the shell, accessibility, DPI, and packaging path quickly. The risk is that XAML virtualization, icon conversion, or thumbnail decode will miss the hard scroll and UI-thread gates.

## Decision

1. Ship version 1 as a C# / .NET 10 / WinUI 3 application.
2. Keep `FilesMate.Core` free of WinUI, Win32, COM, and package-specific types.
3. Put filesystem, icon, thumbnail, watcher, and shell work behind Core-owned interfaces implemented in `FilesMate.Platform.Windows`.
4. Render files behind `IFileSurface`. Start with `ItemsRepeater` details and grid views.
5. If the managed file surface still misses hard gates after profiling and two focused optimizations, replace only that surface with a C++/WinRT Direct2D/DirectWrite component. Do not rewrite the application shell, session model, or schedulers.

## Consequences

- Delivery of navigation, operations, and accessibility is not blocked on a custom engine.
- Hot-path isolation is mandatory from Task 0: no per-file ViewModels, no UI-thread filesystem/COM, no unbounded caches.
- A later native surface is an evidence-driven module swap, not a stack change. Changing OS/CPU targets or replacing WinUI as the primary shell still requires an explicit user decision.

## Alternatives considered

- Custom renderer from day one (File Pilot style): highest performance ceiling, slowest path to a complete daily-use manager.
- Electron / MAUI / WPF / Avalonia: rejected; they either add a browser runtime or fail the native WinUI decision in the plan.
- Full C++ rewrite: rejected until measurements prove the entire managed stack is the limiter.
