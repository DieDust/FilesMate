using Windows.ApplicationModel.DataTransfer;

namespace FilesMate.App.Controls.FileSurface;

public sealed record FileDropRequest(
    IReadOnlyList<string> Paths,
    string? TargetDirectory,
    DataPackageOperation Operation,
    bool FromShelf = false,
    bool AllowSameDirectoryCopy = false)
{
    public const string ShelfMarker = "FilesMate.FileShelf";
    private const string PathsMarker = "FilesMate.DragPaths";

    public static void SetSourcePaths(DataPackage data, IReadOnlyList<string> paths) =>
        data.Properties[PathsMarker] = System.Text.Json.JsonSerializer.Serialize(paths);

    public static IReadOnlyList<string> SourcePaths(DataPackageView data)
    {
        if (!data.Properties.TryGetValue(PathsMarker, out var value) || value is not string json || json.Length > 1024 * 1024)
            return [];
        try
        {
            return (System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)).ToArray();
        }
        catch (System.Text.Json.JsonException) { return []; }
    }
}
