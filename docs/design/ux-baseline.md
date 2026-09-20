# Visual regression scenes

Use synthetic fixtures in an isolated test profile, not personal folders. Capture light, dark and high-contrast modes at 1280×720, 1440×900 and 1920×1080. Record Windows build, app commit, DPI and GPU; compare like-for-like captures.

Cover empty folders, a mixed folder with files and subfolders, and a generated large-folder dataset. For each applicable scene, inspect no selection, hover, single and multiple selection, menus, loading and error states. Check keyboard focus, clipped text, side previews, split panes and dialogs at narrow widths.

`scripts/capture-ui.ps1` captures a visible, unminimized Release window from this checkout and records its DPI. Supply the exact current window title, theme and desired client size. Keep evidence under ignored `artifacts/` paths. Do not change global display settings just to make a capture pass.

The images in [the README](../../README.md) are feature demonstrations, not evidence that every visual regression scene has passed. They were captured from the real 1.1.81 interface using synthetic files.
