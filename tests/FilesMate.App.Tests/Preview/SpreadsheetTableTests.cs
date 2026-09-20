using System.IO.Compression;
using System.Text;
using ExcelDataReader;
using FilesMate.SearchHost;

namespace FilesMate.App.Tests.Preview;

public sealed class SpreadsheetTableTests
{
    [Fact]
    public void Merged_text_uses_the_combined_columns_without_duplicate_cells()
    {
        var html = Render("<cols><col min='1' max='3' width='20' customWidth='1'/></cols><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>Long merged text</t></is></c><c r='C1'/></row></sheetData><mergeCells><mergeCell ref='A1:C3'/></mergeCells>", "A1:C3");
        Assert.Contains("rowspan='3' colspan='3'>Long merged text", html);
        Assert.Equal(1, Count(html, "<td"));
        Assert.Equal(3, Count(html, "<tr "));
    }

    [Fact]
    public void Merge_extending_beyond_the_last_stored_cell_is_not_cut_off()
    {
        var html = Render("<sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>Title</t></is></c></row></sheetData><mergeCells><mergeCell ref='A1:C2'/></mergeCells>", "A1:C2");
        Assert.Contains("rowspan='2' colspan='3'>Title", html);
        Assert.Equal(3, Count(html, "<col style="));
    }

    [Fact]
    public void Wide_sheets_scroll_instead_of_squeezing_columns_into_the_card()
    {
        var html = Render("<cols><col min='1' max='2' width='60' customWidth='1'/></cols><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>Left</t></is></c><c r='B1' t='inlineStr'><is><t>Right</t></is></c></row></sheetData>", "A1:B1");
        Assert.Contains("width:850px", html);
        Assert.Contains("overflow:auto", html);
        Assert.Contains("table-layout:fixed;max-width:none", html);
    }

    [Fact]
    public void Empty_formatted_tail_does_not_push_pictures_hundreds_of_rows_down()
    {
        var html = Render("<sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>Value</t></is></c></row><row r='400'><c r='Z400'/></row></sheetData>", "A1:Z400");
        Assert.Equal(1, Count(html, "<tr "));
        Assert.Equal(1, Count(html, "<col style="));
    }

    [Fact]
    public void Cell_text_is_escaped_and_workbook_size_is_bounded()
    {
        var html = Render("<sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>&lt;script&gt;bad&lt;/script&gt;</t></is></c></row><row r='501'><c r='BM501' t='inlineStr'><is><t>Outside preview</t></is></c></row></sheetData>", "A1:BM501");
        Assert.Contains("&lt;script&gt;bad&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("Outside preview", html);
        Assert.Contains(FilesMate.App.Localization.StringTable.Html("Preview_SheetTruncated"), html);
    }

    private static int Count(string value, string token) => value.Split(token).Length - 1;

    private static string Render(string sheet, string dimension)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var data = new MemoryStream();
        using (var archive = new ZipArchive(data, ZipArchiveMode.Create, true))
        {
            void Add(string path, string xml) { using var writer = new StreamWriter(archive.CreateEntry(path).Open()); writer.Write(xml); }
            Add("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='xml' ContentType='application/xml'/><Override PartName='/xl/workbook.xml' ContentType='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml'/></Types>");
            Add("_rels/.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='xl/workbook.xml'/></Relationships>");
            Add("xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Sheet' sheetId='1' r:id='r1'/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet' Target='worksheets/sheet1.xml'/></Relationships>");
            Add("xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><dimension ref='" + dimension + "'/>" + sheet + "</worksheet>");
        }
        data.Position = 0;
        using var reader = ExcelReaderFactory.CreateReader(data);
        return SpreadsheetTable.Render(reader);
    }
}
