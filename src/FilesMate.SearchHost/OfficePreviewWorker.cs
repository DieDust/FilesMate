using Loc = FilesMate.App.Localization.StringTable;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocSharp.Docx;
using ExcelDataReader;

namespace FilesMate.SearchHost;

/// <summary>Short-lived, non-interactive Office rendering entry point.</summary>
internal static class OfficePreviewWorker
{
    internal static int Run(string path, string outputDirectory)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            if (new FileInfo(path).Length > 64 * 1024 * 1024) return 2;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var intermediate = Path.Combine(outputDirectory, "converted.docx");
            if (extension == ".doc")
            {
                using var reader = new DocSharp.Binary.StructuredStorage.Reader.StructuredStorageReader(path);
                var doc = new DocSharp.Binary.DocFileFormat.WordDocument(reader);
                using (var docx = DocSharp.Binary.OpenXmlLib.WordprocessingML.WordprocessingDocument.Create(intermediate, DocSharp.Binary.OpenXmlLib.WordprocessingDocumentType.Document))
                    DocSharp.Binary.WordprocessingMLMapping.Converter.Convert(doc, docx);
                path = intermediate;
            }
            else if (extension == ".rtf")
            {
                new RtfToDocxConverter().Convert(path, intermediate);
                path = intermediate;
            }
            else if (extension == ".ppt")
            {
                intermediate = Path.Combine(outputDirectory, "converted.pptx");
                using var reader = new DocSharp.Binary.StructuredStorage.Reader.StructuredStorageReader(path);
                var ppt = new DocSharp.Binary.PptFileFormat.PowerpointDocument(reader);
                using (var pptx = DocSharp.Binary.OpenXmlLib.PresentationML.PresentationDocument.Create(intermediate, DocSharp.Binary.OpenXmlLib.PresentationDocumentType.Presentation))
                    DocSharp.Binary.PresentationMLMapping.Converter.Convert(ppt, pptx);
                path = intermediate;
            }
            var output = Path.Combine(outputDirectory, "preview.html");
            if (extension is ".doc" or ".docx" or ".rtf")
            {
                CheckArchive(path);
                // No original folder: external sub-documents are not loaded.
                using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
                // Some legacy DOC converters emit empty/"auto" shading enums.
                // Normalize only the in-memory preview; the original stays read-only.
                var parts = new Queue<DocumentFormat.OpenXml.Packaging.OpenXmlPart>(document.Parts.Select(p => p.OpenXmlPart));
                var visited = new HashSet<Uri>();
                while (parts.TryDequeue(out var part))
                {
                    if (!visited.Add(part.Uri)) continue;
                    foreach (var child in part.Parts) parts.Enqueue(child.OpenXmlPart);
                    if (part.RootElement is not { } root) continue;
                    foreach (var shading in root.Descendants<DocumentFormat.OpenXml.Wordprocessing.Shading>())
                    {
                        try { _ = shading.Val?.Value; }
                        catch (FormatException) { shading.Val = DocumentFormat.OpenXml.Wordprocessing.ShadingPatternValues.Clear; }
                    }
                }
                try
                {
                    using var writer = new StreamWriter(output, false, new UTF8Encoding(false));
                    new DocxToHtmlConverter { FixedLayout = false, OriginalFolderPath = null }.Convert(document, writer);
                }
                catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
                {
                    File.WriteAllText(output, SimpleWord(document));
                }
            }
            else if (extension is ".xls" or ".xlsx")
            {
                if (extension == ".xlsx") CheckArchive(path);
                File.WriteAllText(output, Spreadsheet(path, outputDirectory));
            }
            else if (extension is ".pptx" or ".ppt")
            {
                CheckArchive(path);
                File.WriteAllText(output, Presentation(path));
            }
            else return 2;
            if (new FileInfo(output).Length > 24 * 1024 * 1024) return 2;
            var html = File.ReadAllText(output);
            var head = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
            if (head < 0) return 2;
            html = html.Insert(head + 6, """
                <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; object-src 'none'">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <style>html{background:#fff;color:#202020;color-scheme:light}body{margin:0!important;padding:18px!important;font:14px/1.65 'Segoe UI','Microsoft YaHei UI',sans-serif;overflow-wrap:anywhere}img{max-width:100%;height:auto}table{border-collapse:collapse;max-width:100%}td,th{padding:5px 8px;border:1px solid #ddd}a{color:#0067c0}section{margin-bottom:24px}h2{font-size:18px}.note{color:#666;font-size:12px}figure{margin:16px 0}.chart svg{width:100%;max-height:300px}::-webkit-scrollbar{width:10px;height:10px}::-webkit-scrollbar-thumb{background:#8889;border:2px solid transparent;background-clip:padding-box;border-radius:8px}::-webkit-scrollbar-corner{background:transparent}::-webkit-scrollbar-button{display:none}</style>
                """);
            File.WriteAllText(output, html, new UTF8Encoding(false));
            return 0;
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.TraceError(e.ToString());
            try { File.WriteAllText(Path.Combine(outputDirectory, "error.txt"), e.ToString()); } catch { }
            return 1;
        }
    }

    private static void CheckArchive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 128 * 1024 * 1024)
            throw new InvalidDataException("Office document exceeds preview limits.");
    }

    private static string SimpleWord(DocumentFormat.OpenXml.Packaging.WordprocessingDocument document)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var root = XElement.Parse(document.MainDocumentPart!.Document!.OuterXml);
        string Render(XElement node) => node.Name.LocalName switch
        {
            "t" => WebUtility.HtmlEncode(node.Value),
            "tab" => "&emsp;",
            "br" => "<br>",
            "p" => "<p>" + string.Concat(node.Elements().Select(Render)) + "</p>",
            "tbl" => "<table>" + string.Concat(node.Elements().Select(Render)) + "</table>",
            "tr" => "<tr>" + string.Concat(node.Elements().Select(Render)) + "</tr>",
            "tc" => "<td>" + string.Concat(node.Elements().Select(Render)) + "</td>",
            "r" => (node.Element(w + "rPr")?.Element(w + "b") is not null ? "<strong>" : "<span>")
                + string.Concat(node.Elements().Where(e => e.Name.LocalName != "rPr").Select(Render))
                + (node.Element(w + "rPr")?.Element(w + "b") is not null ? "</strong>" : "</span>"),
            "body" or "document" or "hyperlink" or "sdt" or "sdtContent" => string.Concat(node.Elements().Select(Render)),
            _ => ""
        };
        return "<!doctype html><html><head></head><body><p class='note'>" + Loc.Html("Preview_Simplified") + "</p>" + Render(root) + "</body></html>";
    }

    private static string Spreadsheet(string path, string outputDirectory)
    {
        var visualPath = path;
        var visualWarning = "";
        var pictures = new Dictionary<string, LegacySpreadsheetPictures.SheetData>(StringComparer.Ordinal);
        if (Path.GetExtension(path).Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            try { pictures = LegacySpreadsheetPictures.Read(path); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { visualWarning = "<p class='note'>" + Loc.Html("Preview_LegacyImages") + "</p>"; }
            // Convert drawings in the bounded, disposable worker. Read cells from
            // the original workbook so a conversion failure never loses its data.
            visualPath = Path.Combine(outputDirectory, "converted.xlsx");
            try
            {
                using var storage = new DocSharp.Binary.StructuredStorage.Reader.StructuredStorageReader(path);
                var xls = new DocSharp.Binary.Spreadsheet.XlsFileFormat.XlsDocument(storage);
                using (var xlsx = DocSharp.Binary.OpenXmlLib.SpreadsheetML.SpreadsheetDocument.Create(visualPath, DocSharp.Binary.OpenXmlLib.SpreadsheetDocumentType.Workbook))
                    DocSharp.Binary.SpreadsheetMLMapping.Converter.Convert(xls, xlsx);
                CheckArchive(visualPath);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                visualPath = "";
                visualWarning = "<p class='note'>" + Loc.Html("Preview_LegacyCharts") + "</p>";
            }
        }
        using var visuals = visualPath.Length > 0 ? new OfficeVisuals(visualPath) : null;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var html = new StringBuilder("<!doctype html><html><head></head><body><p class='note'>" + Loc.Html("Preview_SpreadsheetValues") + "</p>");
        var sheets = 0;
        do
        {
            html.Append("<section><h2>").Append(WebUtility.HtmlEncode(reader.Name)).Append("</h2>");
            pictures.TryGetValue(reader.Name, out var sheetData);
            html.Append(SpreadsheetTable.Render(reader, sheetData?.ColumnWidths));
            if (sheetData is not null) html.Append(sheetData.Html);
            if (visuals is not null)
            {
                try { html.Append(visuals.SheetVisuals(reader.Name)); }
                catch (Exception error) when (error is not OutOfMemoryException)
                { html.Append("<p class='note'>" + Loc.Html("Preview_SheetImages") + "</p>"); }
            }
            html.Append("</section>");
            if (html.Length > 4 * 1024 * 1024) break;
        } while (++sheets < 20 && reader.NextResult());
        html.Append(visualWarning).Append("<p class='note'>" + Loc.Html("Preview_SheetLimits") + "</p></body></html>");
        return html.ToString();
    }

    private static string Presentation(string path)
    {
        using var visuals = new OfficeVisuals(path);
        return visuals.Slides();
    }

}
