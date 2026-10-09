namespace FilesMate.App.Controls.FileSurface;

public sealed partial class FileDetailsSurface
{
    private string _nameHighlightQuery = "";

    public void SetNameHighlightQuery(string? query)
    {
        query ??= "";
        if (_nameHighlightQuery == query) return;
        _nameHighlightQuery = query;
        // Each surface owns its pane query. No asynchronous work or global search state.
        foreach (var row in _realized) row.SetNameHighlightQuery(query);
        foreach (var tile in _tiles) tile.SetNameHighlightQuery(query);
    }
}
