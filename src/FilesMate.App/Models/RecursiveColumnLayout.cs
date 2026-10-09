namespace FilesMate.App.Models;

/// <summary>Transient details columns; never mutates the ordinary layout.</summary>
public static class RecursiveColumnLayout
{
    public static DetailsColumn[] Create(IEnumerable<DetailsColumn> ordinary, double viewportWidth)
    {
        var columns = DetailsColumn.Normalize(ordinary);
        var path = columns.First(c => c.Id == DetailsColumnId.Location);
        // Leave the name usable at narrow widths. The normal column resizer
        // remains available, and deep names use the existing horizontal scroll.
        var available = double.IsFinite(viewportWidth) && viewportWidth > 0 ? viewportWidth : 720;
        var width = Math.Clamp(Math.Min(path.Width, Math.Min(144, available * .28)), 64, 144);
        return new[] { path with { Visible = true, Width = width },
            columns.First(c => c.Id == DetailsColumnId.Name) }
            .Concat(columns.Where(c => c.Id is not (DetailsColumnId.Location or DetailsColumnId.Name))).ToArray();
    }
}
