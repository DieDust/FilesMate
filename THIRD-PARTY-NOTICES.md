# Third-Party Notices

FilesMate includes adapted source from third-party open-source projects. Inclusion does not imply endorsement of FilesMate by the original authors.

## Files Community — Files

- Project: https://github.com/files-community/Files
- Pinned revision: `abdfcb543adc7fcdf7f75672feb83760c48d9087`
- Copyright: Copyright (c) 2018-present Files Community
- License for adapted Files.App UI source listed in `docs/upstream/files-community-ui.json`: MIT License

### MIT License

Copyright (c) 2018-present Files Community

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


## Bundled libraries and runtimes

FilesMate's Apache-2.0 license does not replace the following licenses. Exact resolved package versions and package metadata are copied into the installer's ThirdPartyNotices directory. Full texts for libraries whose NuGet packages provide only an SPDX expression are kept in installer/ThirdPartyNotices.

| Component | Resolved version | License / notice |
| --- | --- | --- |
| AvalonEdit | 6.3.1.120 | MIT; AvalonEdit-LICENSE.txt |
| DocSharp (Common, Docx, Binary.Common, Binary.Doc, Binary.Ppt, Binary.Xls) | 0.21.0 | MIT; DocSharp-LICENSE.txt |
| DocumentFormat.OpenXml and Framework | 3.5.1 | MIT; OpenXML-SDK-LICENSE.txt |
| NPOI | 2.7.6 | Apache-2.0; package LICENSE retained; used only by the disposable XLS preview worker |
| ExcelDataReader | 3.9.0 | MIT; ExcelDataReader-LICENSE.txt |
| Markdig | 1.3.2 | BSD-2-Clause; Markdig-LICENSE.txt |
| Mozilla PDF.js (pdfjs-dist) | 6.3.289 | Apache-2.0; Assets/PdfPreview/LICENSE; bundled font/CMap/WASM notices retained in their asset directories |
| Microsoft.Data.Sqlite and Core | 10.0.1 | MIT; Microsoft.Data.Sqlite-LICENSE.txt |
| SQLitePCLRaw bundle/core/provider | 2.1.11 | Apache-2.0; SQLitePCLRaw-LICENSE.txt |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 wrapper/package; SQLite engine is public domain |
| Microsoft.Web.WebView2 | 1.0.3719.77 | Microsoft package LICENSE.txt and NOTICE.txt |
| Microsoft.WindowsAppSDK components | Base 2.0.4, Foundation 2.3.9, WinUI 2.3.6, InteractiveExperiences 2.1.6, DWrite 2.1.0 | Individual package license.txt and available NOTICE.txt |
| .NET / Windows Desktop self-contained runtimes | Resolved by the installed SDK at packaging time | Runtime LICENSE and THIRD-PARTY-NOTICES copied from runtime packs |

License provenance for the two added texts:

- Microsoft.Data.Sqlite: https://github.com/dotnet/efcore/blob/v10.0.1/LICENSE.txt
- SQLitePCLRaw: https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.11/LICENSE.TXT

Windows SDK and WinApp build tools are build dependencies, not FilesMate-owned source. Their NuGet metadata and available license texts are retained when generating the installer notice inventory. Microsoft redistribution terms continue to apply to redistributed Microsoft components.

The checked-in source manifest for adapted UI is docs/upstream/files-community-ui.json. See [asset provenance](docs/assets.md) for project-generated branding, generic file artwork and screenshots. Runtime icons retrieved from Windows are not grants to redistribute the corresponding application artwork independently.
# Offline Mandarin name data

The name-sort table `src/FilesMate.Core/Entries/Data/mandarin-17.bin` is derived from Unicode 17.0.0 Unihan `kMandarin` data (44,348 characters). It uses dictionary-primary readings, without contextual polyphone inference. Unicode License V3 is included in `installer/ThirdPartyNotices/Unicode-LICENSE.txt` and distributed with the installer. See `scripts/generate-pinyin-data.py` for the pinned source URL, SHA-256 check and reproducible generator. Source: https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip . License: https://www.unicode.org/license.txt .
