# WPF icon exports

The SVGs in `src/FilesMate.App/Assets/FileIcons` are the source artwork.
SearchHost uses 128 px PNG exports because WPF does not natively display SVG.
The app's existing `FileTypeIconCatalog.cs` is linked into SearchHost, so file
extensions and artwork names have a single source of truth.

After editing an SVG, run:

```powershell
npm ci --prefix tools/FilesMate.IconRasterizer
node tools/FilesMate.IconRasterizer/render.mjs
node tools/FilesMate.IconRasterizer/render.mjs --check
```

Commit the generated PNGs in `src/FilesMate.SearchHost/Assets/FileIcons` along
with the SVG changes. Node and resvg-js are development tools only; neither is
loaded by SearchHost or distributed in the installer. At 36 DIPs these assets
cover display scaling up to 350% without upscaling.
