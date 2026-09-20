using Loc = FilesMate.App.Localization.StringTable;
using System.Text;
using NPOI.HSSF.UserModel;

namespace FilesMate.SearchHost;

internal static class LegacySpreadsheetPictures
{
    internal sealed record SheetData(string Html, double[] ColumnWidths);
    // Drawing conversion does not preserve XLS pictures. Read the embedded
    // raster data directly, and release the workbook before rendering cells.
    internal static Dictionary<string, SheetData> Read(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var workbook = new HSSFWorkbook(stream);
        var result = new Dictionary<string, SheetData>(StringComparer.Ordinal);
        long bytes = 0;
        for (var i = 0; i < Math.Min(20, workbook.NumberOfSheets); i++)
        {
            var sheet = (HSSFSheet)workbook.GetSheetAt(i);
            var html = new StringBuilder();
            if (sheet.DrawingPatriarch is HSSFPatriarch drawing)
            {
                var shapes = new Queue<(HSSFShape Shape, int Depth)>(drawing.Children.Take(512).Select(s => (s, 0)));
                var visited = 0; var pictures = 0;
                while (shapes.TryDequeue(out var item) && visited++ < 512 && pictures < 24)
                {
                    if (item.Shape is HSSFPicture picture)
                    {
                        pictures++;
                        var image = OfficeVisuals.RasterImage(picture.PictureData.Data, ref bytes);
                        if (image.Length > 0) html.Append("<figure>").Append(image).Append("</figure>");
                        else html.Append("<p class='note'>" + Loc.Html("Preview_ImageUnsupported") + "</p>");
                    }
                    else if (item.Shape is HSSFShapeGroup group && item.Depth < 8)
                        foreach (var child in group.Children.Take(Math.Max(0, 512 - visited - shapes.Count))) shapes.Enqueue((child, item.Depth + 1));
                }
            }
            result[sheet.SheetName] = new(html.ToString(), Enumerable.Range(0, 64).Select(c => sheet.IsColumnHidden(c) ? 0 : sheet.GetColumnWidth(c) / 256d).ToArray());
        }
        return result;
    }
}
