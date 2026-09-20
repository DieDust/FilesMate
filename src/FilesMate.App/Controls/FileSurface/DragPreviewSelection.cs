namespace FilesMate.App.Controls.FileSurface;

internal static class DragPreviewSelection
{
    // The caller supplies the selected items in their displayed list order.
    public static IReadOnlyList<string> Paths(IReadOnlyList<string> selection)
    {
        List<string> result = new(3);
        foreach (var path in selection)
        {
            if (result.Count == 3) break;
            if (!result.Contains(path, StringComparer.OrdinalIgnoreCase)) result.Add(path);
        }
        return result;
    }
}
