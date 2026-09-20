using System.Text.Json;

namespace FilesMate.Search;

public sealed record FeatureSetup(bool Completed = false, bool FavoritesBarEnabled = false, bool SidebarCollapsed = false, double? FavoritesManagerHeight = null)
{
    public static string FilePath(string? profile = null) => Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "feature-setup.json");

    public static FeatureSetup Load(string? profile = null)
    {
        try { return JsonSerializer.Deserialize<FeatureSetup>(File.ReadAllText(FilePath(profile))) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string? profile = null)
    {
        var path = FilePath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
