using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Core.Entries;

namespace FilesMate.App.Models;

public enum DetailsColumnId { Name, Modified, Type, Size, Created, Extension, Attributes, Accessed, Location, FullPath }

public sealed record DetailsColumn(DetailsColumnId Id, double Width, bool Visible = true)
{
    public static DetailsColumn[] Defaults() =>
    [new(DetailsColumnId.Name, 240), new(DetailsColumnId.Modified, 148), new(DetailsColumnId.Type, 100),
     new(DetailsColumnId.Size, 88), new(DetailsColumnId.Created, 148, false),
     new(DetailsColumnId.Extension, 100, false), new(DetailsColumnId.Attributes, 180, false), new(DetailsColumnId.Accessed, 148, false),
     new(DetailsColumnId.Location, 260, false), new(DetailsColumnId.FullPath, 360, false)];

    public static DetailsColumn[] Normalize(IEnumerable<DetailsColumn>? columns)
    {
        var result = new List<DetailsColumn>();
        var seen = new HashSet<DetailsColumnId>();
        foreach (var column in (columns ?? []).Take(32).Concat(Defaults()))
        {
            if (column is null || !Enum.IsDefined(column.Id) || !seen.Add(column.Id)) continue;
            var width = double.IsFinite(column.Width) ? column.Width : Defaults().Single(c => c.Id == column.Id).Width;
            result.Add(column with { Width = Math.Clamp(width, column.Id == DetailsColumnId.Name ? 96 : 64, 560),
                Visible = column.Id == DetailsColumnId.Name || column.Visible });
        }
        return result.ToArray();
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Title => Id switch
    {
        DetailsColumnId.Name => Loc.Get("Column_Name"), DetailsColumnId.Modified => Loc.Get("Column_Modified"), DetailsColumnId.Type => Loc.Get("Column_Type"),
        DetailsColumnId.Size => Loc.Get("Column_Size"), DetailsColumnId.Created => Loc.Get("Preview_Created"), DetailsColumnId.Extension => Loc.Get("Extension"), DetailsColumnId.Accessed => Loc.Get("DateAccessed"),
        DetailsColumnId.Location => Loc.Get("ParentFolder"), DetailsColumnId.FullPath => Loc.Get("FullPath"), _ => Loc.Get("Preview_Attributes")
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public EntrySortColumn Sort => Id switch
    {
        DetailsColumnId.Modified => EntrySortColumn.Modified, DetailsColumnId.Type => EntrySortColumn.Type,
        DetailsColumnId.Size => EntrySortColumn.Size, DetailsColumnId.Created => EntrySortColumn.Created,
        DetailsColumnId.Extension => EntrySortColumn.Extension, DetailsColumnId.Attributes => EntrySortColumn.Attributes,
        DetailsColumnId.Accessed => EntrySortColumn.Accessed, DetailsColumnId.Location => EntrySortColumn.Location,
        DetailsColumnId.FullPath => EntrySortColumn.FullPath, _ => EntrySortColumn.Name
    };
}
