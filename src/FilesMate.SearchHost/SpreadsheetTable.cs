using Loc = FilesMate.App.Localization.StringTable;
using System.Globalization;
using System.Net;
using System.Text;
using ExcelDataReader;

namespace FilesMate.SearchHost;

/// <summary>Bounded sheet layout, preserving column widths and merged cells.</summary>
internal static class SpreadsheetTable
{
    internal static string Render(IExcelDataReader reader, double[]? columnWidths = null)
    {
        const int rowLimit = 500, columnLimit = 64, textLimit = 1_000_000;
        var merges = (reader.MergeCells ?? []).Take(4096).ToArray();
        var mergedColumns = merges.Length == 0 ? 0 : (int)Math.Min(columnLimit, (long)merges.Max(m => m.ToColumn) + 1);
        var columns = Math.Min(columnLimit, Math.Max(reader.FieldCount, mergedColumns));
        var widths = Enumerable.Range(0, columns).Select(c =>
        {
            var width = columnWidths is not null && c < columnWidths.Length ? columnWidths[c]
                : c < reader.FieldCount ? reader.GetColumnWidth(c) : 8.43;
            return width <= 0 ? 0 : (int)Math.Clamp(Math.Round(width * 7 + 5), 24, 480);
        }).ToArray();
        var rows = new List<(string[] Values, double Height)>();
        var lastColumn = -1; var lastRow = -1; var characters = 0;
        while (rows.Count < rowLimit && reader.Read())
        {
            var values = new string[columns];
            for (var c = 0; c < columns; c++)
            {
                var value = c < reader.FieldCount ? reader.GetValue(c) : null;
                var text = value is DateTime date ? date.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
                    : Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
                values[c] = text.Length > 4000 ? text[..4000] + "…" : text;
                characters += values[c].Length;
                if (text.Length > 0) { lastColumn = Math.Max(lastColumn, c); lastRow = rows.Count; }
            }
            rows.Add((values, reader.RowHeight));
            if (characters >= textLimit) break;
        }

        var covered = new bool[rowLimit, columnLimit];
        var anchors = new Dictionary<(int Row, int Column), (int Rows, int Columns)>();
        foreach (var merge in merges)
        {
            var r = merge.FromRow; var c = merge.FromColumn;
            if (r < 0 || c < 0 || r >= rows.Count || c >= columns || covered[r, c] ||
                string.IsNullOrEmpty(rows[r].Values[c])) continue;
            var bottom = Math.Min(rowLimit - 1, merge.ToRow);
            var right = Math.Min(columns - 1, merge.ToColumn);
            if (bottom < r || right < c) continue;
            anchors[(r, c)] = (bottom - r + 1, right - c + 1);
            for (var y = r; y <= bottom; y++)
                for (var x = c; x <= right; x++) covered[y, x] = true;
            lastRow = Math.Max(lastRow, bottom); lastColumn = Math.Max(lastColumn, right);
        }

        var html = new StringBuilder("<style>.sheet-scroll{overflow:auto;max-width:100%;margin:8px 0}.sheet-grid{table-layout:fixed;max-width:none}.sheet-grid td{box-sizing:border-box;vertical-align:top;white-space:pre-wrap;overflow-wrap:break-word;word-break:normal}.sheet-grid col.hidden{visibility:collapse}</style>");
        if (lastRow >= 0)
        {
            html.Append("<div class='sheet-scroll'><table class='sheet-grid' style='width:")
                .Append(widths.Take(lastColumn + 1).Sum()).Append("px'><colgroup>");
            for (var c = 0; c <= lastColumn; c++)
                html.Append("<col style='width:").Append(widths[c]).Append("px'")
                    .Append(widths[c] == 0 ? " class='hidden'>" : ">");
            html.Append("</colgroup><tbody>");
            for (var r = 0; r <= lastRow; r++)
            {
                var height = r < rows.Count ? rows[r].Height : 15;
                html.Append(height <= 0 ? "<tr style='display:none'>" : "<tr style='height:" + Math.Clamp(height * 96 / 72, 20, 300).ToString("0.##", CultureInfo.InvariantCulture) + "px'>");
                for (var c = 0; c <= lastColumn; c++)
                {
                    var merged = anchors.TryGetValue((r, c), out var span);
                    if (covered[r, c] && !merged) continue;
                    html.Append("<td");
                    if (merged) html.Append(" rowspan='").Append(span.Rows).Append("' colspan='").Append(span.Columns).Append("'");
                    html.Append('>').Append(WebUtility.HtmlEncode(r < rows.Count ? rows[r].Values[c] : "")).Append("</td>");
                }
                html.Append("</tr>");
            }
            html.Append("</tbody></table></div>");
        }
        if (reader.RowCount > rowLimit || reader.FieldCount > columnLimit || characters >= textLimit)
            html.Append("<p class='note'>" + Loc.Html("Preview_SheetTruncated") + "</p>");
        return html.ToString();
    }
}
