using Loc = FilesMate.App.Localization.StringTable;
using FilesMate.Core.Entries;

namespace FilesMate.App.Models;

public enum DetailsColumnId { Name, Modified, Type, Size, Created, Extension, Attributes, Accessed, Location, FullPath, Tags, ShellProperty }

public sealed record DetailsColumn(DetailsColumnId Id, double Width, bool Visible = true, string? PropertyName = null, string? PropertyTitle = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => Id == DetailsColumnId.ShellProperty ? "shell:" + PropertyName : Id.ToString();

    public static bool IsPropertyName(string? name) => name is { Length: > 0 and <= 200 }
        && name.Contains('.') && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public static DetailsColumn[] Defaults() =>
    [new(DetailsColumnId.Name, 240), new(DetailsColumnId.Modified, 148), new(DetailsColumnId.Type, 100),
     new(DetailsColumnId.Size, 88), new(DetailsColumnId.Tags, 176), new(DetailsColumnId.Created, 148, false),
     new(DetailsColumnId.Extension, 100, false), new(DetailsColumnId.Attributes, 180, false), new(DetailsColumnId.Accessed, 148, false),
     new(DetailsColumnId.Location, 260, false), new(DetailsColumnId.FullPath, 360, false)];

    public static DetailsColumn[] Normalize(IEnumerable<DetailsColumn>? columns)
    {
        var result = new List<DetailsColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in (columns ?? []).Take(1024).Concat(Defaults()))
        {
            if (column is null || !Enum.IsDefined(column.Id) || (column.Id == DetailsColumnId.ShellProperty && !IsPropertyName(column.PropertyName)) || !seen.Add(column.Key)) continue;
            var width = double.IsFinite(column.Width) ? column.Width : Defaults().FirstOrDefault(c => c.Id == column.Id)?.Width ?? 160;
            result.Add(column with { Width = Math.Clamp(width, column.Id == DetailsColumnId.Name ? 96 : 64, 1200),
                Visible = column.Id == DetailsColumnId.Name || column.Visible,
                PropertyTitle = column.PropertyTitle is { Length: > 200 } title ? title[..200] : column.PropertyTitle });
        }
        return result.ToArray();
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Title => Id switch
    {
        DetailsColumnId.Name => Loc.Get("Column_Name"), DetailsColumnId.Modified => Loc.Get("Column_Modified"), DetailsColumnId.Type => Loc.Get("Column_Type"),
        DetailsColumnId.Size => Loc.Get("Column_Size"), DetailsColumnId.Created => Loc.Get("Preview_Created"), DetailsColumnId.Extension => Loc.Get("Extension"), DetailsColumnId.Accessed => Loc.Get("DateAccessed"),
        DetailsColumnId.Location => Loc.Get("ParentFolder"), DetailsColumnId.FullPath => Loc.Get("FullPath"),
        DetailsColumnId.Tags => Loc.Get("TagsTitle"),
        DetailsColumnId.ShellProperty => PropertyTitle ?? PropertyName ?? "", _ => Loc.Get("Preview_Attributes")
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanSort => Id != DetailsColumnId.Tags;

    [System.Text.Json.Serialization.JsonIgnore]
    public EntrySortColumn Sort => Id switch
    {
        DetailsColumnId.Modified => EntrySortColumn.Modified, DetailsColumnId.Type => EntrySortColumn.Type,
        DetailsColumnId.Size => EntrySortColumn.Size, DetailsColumnId.Created => EntrySortColumn.Created,
        DetailsColumnId.Extension => EntrySortColumn.Extension, DetailsColumnId.Attributes => EntrySortColumn.Attributes,
        DetailsColumnId.Accessed => EntrySortColumn.Accessed, DetailsColumnId.Location => EntrySortColumn.Location,
        DetailsColumnId.FullPath => EntrySortColumn.FullPath, DetailsColumnId.ShellProperty => EntrySortColumn.ShellProperty,
        _ => EntrySortColumn.Name
    };

    public bool IsSortedBy(EntrySort sort) => CanSort && Sort == sort.Column
        && (Id != DetailsColumnId.ShellProperty || PropertyName == sort.PropertyName);
}
