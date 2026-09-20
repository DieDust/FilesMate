using System.Text.Json;

namespace FilesMate.Search;

public sealed record SearchPreviewSize(double Width, double Height)
{
    public SearchPreviewSize Normalize() => new(
        double.IsFinite(Width) ? Math.Clamp(Width, 240, 1600) : 400,
        double.IsFinite(Height) ? Math.Clamp(Height, 200, 1600) : 368);

    public static SearchPreviewSize? Load(string profile)
    {
        try
        {
            var path = Path.Combine(profile, "search-preview-size.json");
            if (new FileInfo(path).Length > 4096) return null;
            return JsonSerializer.Deserialize<SearchPreviewSize>(File.ReadAllText(path))?.Normalize();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(string profile)
    {
        Directory.CreateDirectory(profile);
        var path = Path.Combine(profile, "search-preview-size.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Normalize()));
        File.Move(temporary, path, overwrite: true);
    }
}
