using System.IO.Compression;
using System.Text;

namespace FilesMate.Core.Operations;

public enum NewDocumentKind { Text, Word, Spreadsheet, Presentation, Markdown, Csv, RichText, Html, Json }

/// <summary>Small, valid blank documents; binary document types are never zero-byte placeholders.</summary>
public static class NewDocumentTemplate
{
    public static string Extension(NewDocumentKind kind) => kind switch
    {
        NewDocumentKind.Word => ".docx", NewDocumentKind.Spreadsheet => ".xlsx",
        NewDocumentKind.Presentation => ".pptx", NewDocumentKind.Markdown => ".md",
        NewDocumentKind.Csv => ".csv", NewDocumentKind.RichText => ".rtf",
        NewDocumentKind.Html => ".html", NewDocumentKind.Json => ".json", _ => ".txt"
    };
    public static byte[] Content(NewDocumentKind kind)
    {
        if (kind is not (NewDocumentKind.Word or NewDocumentKind.Spreadsheet or NewDocumentKind.Presentation))
            return Encoding.UTF8.GetBytes(kind switch
            {
                NewDocumentKind.RichText => "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Arial;}}\\f0\\fs22\\pard\\par}",
                NewDocumentKind.Html => "<!doctype html>\n<html lang=\"en\">\n<head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title></title></head>\n<body>\n</body>\n</html>\n",
                NewDocumentKind.Json => "{}\n", _ => ""
            });
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var main = kind switch { NewDocumentKind.Word => "word/document.xml", NewDocumentKind.Spreadsheet => "xl/workbook.xml", _ => "ppt/presentation.xml" };
            var contentType = kind switch { NewDocumentKind.Word => "wordprocessingml.document", NewDocumentKind.Spreadsheet => "spreadsheetml.sheet", _ => "presentationml.presentation" };
            var extraType = kind == NewDocumentKind.Spreadsheet
                ? "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" : "";
            Add("[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/{main}\" ContentType=\"application/vnd.openxmlformats-officedocument.{contentType}.main+xml\"/>{extraType}</Types>");
            Add("_rels/.rels", Relationships($"<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"{main}\"/>"));
            if (kind == NewDocumentKind.Word)
                Add(main, "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/><w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
            else if (kind == NewDocumentKind.Spreadsheet)
            {
                Add(main, "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Add("xl/_rels/workbook.xml.rels", Relationships("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"));
                Add("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData/></worksheet>");
            }
            else
                Add(main, "<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:sldSz cx=\"12192000\" cy=\"6858000\" type=\"screen16x9\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"); writer.Write(xml);
            }
        }
        return buffer.ToArray();
    }
    private static string Relationships(string items) => "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + items + "</Relationships>";
}
