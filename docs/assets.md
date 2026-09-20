# Asset provenance

This inventory describes the assets in the initial public source snapshot. Third-party package licenses remain in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) and `installer/ThirdPartyNotices`.

| Asset | Source and reproduction | Terms |
| --- | --- | --- |
| FilesMate logo and installer/application icons | Original geometric mark in `tools/FilesMate.BrandAssets/IconMark.cs`; SVG and PNG/ICO output under `src/FilesMate.App/Assets/Branding` | Project Apache-2.0 license; not the Files Community logo |
| Generic file/folder artwork | Geometric artwork and file-type letter labels in `tools/FilesMate.BrandAssets` and `src/FilesMate.App/Assets/FileIcons/*.svg` | Project Apache-2.0 license |
| Search-host file icons | PNG exports of the above SVG files; `tools/FilesMate.IconRasterizer/render.mjs` | Same as source artwork; rasterizer is a development dependency |
| Flow plugin icon | FilesMate application branding under `src/FilesMate.FlowPlugin/Images` | Project Apache-2.0 license |
| Adapted tab style | Files Community source pinned and attributed in `docs/upstream/files-community-ui.json` | MIT, attribution retained |
| PDF preview runtime, fonts, CMaps and WASM | Pinned `pdfjs-dist`, integrity checked by `scripts/vendor-pdf-preview.py`; version recorded in `Assets/PdfPreview/VERSION.txt` | Apache-2.0 and bundled component notices in the asset directories |
| Mandarin sorting table | Unicode Unihan data generated with `scripts/generate-pinyin-data.py` | Unicode License V3, included in installer notices |
| Product screenshots | Actual UI with synthetic files in a separate test profile; `docs/images`. Media examples use original geometric landscape test images and an MP4 made from one of those images. | Project documentation assets; no personal documents used |
| QQ invitation | Maintainer-supplied image for community group 984027951 | Invitation artwork, not a grant to reuse Tencent/QQ trademarks as product branding |

Runtime Windows file-association icons are obtained from the user's system, not a bundled collection of third-party application logos. Naming a file type does not imply endorsement by its vendor. Documentation screenshots may show platform UI and product names for identification.
