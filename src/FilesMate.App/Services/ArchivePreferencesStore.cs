using System.Text.Json;
using FilesMate.Platform.Windows.Archives;

namespace FilesMate.App.Services;

internal sealed record ArchivePreferences(ArchiveProvider Preferred = ArchiveProvider.Automatic,
    Dictionary<ArchiveProvider, string>? Executables = null)
{
    public string? PathFor(ArchiveProvider provider) => Executables?.GetValueOrDefault(provider);
}

internal sealed class ArchivePreferencesStore(string path)
{
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate", "archives.json");
    internal ArchivePreferences Load()
    {
        try
        {
            if (!File.Exists(path)) return new();
            var value = JsonSerializer.Deserialize<ArchivePreferences>(File.ReadAllText(path));
            return value is not null && Enum.IsDefined(value.Preferred) ? value : new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(ArchivePreferences value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
