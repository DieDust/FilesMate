using System.Text.Json;

namespace FilesMate.Search;

public sealed record HiddenSearchResult(string Key, string Name, string Location);

public static class HiddenSearchResults
{
    private static readonly object Gate = new();
    private static string FilePath(string? profile) => Path.Combine(profile ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate"), "hidden-search-results.json");
    public static string FileKey(string path) => "file:" + path.ToUpperInvariant();
    public static string Key(NameHit hit) => hit.Application is { } app ? "app:" + app.Identity : FileKey(hit.Path);
    public static bool Contains(IReadOnlyDictionary<string, HiddenSearchResult> hidden, NameHit hit) =>
        hidden.ContainsKey(Key(hit)) || (hit.Application?.ShortcutPath is { } shortcut && hidden.ContainsKey(FileKey(shortcut)));

    public static IReadOnlyDictionary<string, HiddenSearchResult> Load(string? profile = null)
    {
        lock (Gate)
        {
            try
            {
                return (JsonSerializer.Deserialize<HiddenSearchResult[]>(File.ReadAllText(FilePath(profile))) ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key)).DistinctBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { return new Dictionary<string, HiddenSearchResult>(StringComparer.OrdinalIgnoreCase); }
        }
    }

    public static void Hide(IEnumerable<NameHit> hits, string? profile = null)
    {
        lock (Gate)
        {
            var items = Load(profile).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var hit in hits)
                items[Key(hit)] = new(Key(hit), hit.Name, hit.Application?.LocationPath ?? (hit.Application is null ? hit.Path : "Windows 应用入口"));
            Save(items.Values, profile);
        }
    }

    public static void Restore(IEnumerable<string> keys, string? profile = null)
    {
        lock (Gate)
        {
            var removed = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Save(Load(profile).Values.Where(item => !removed.Contains(item.Key)), profile);
        }
    }

    private static void Save(IEnumerable<HiddenSearchResult> items, string? profile)
    {
        var path = FilePath(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(items.ToArray()));
        File.Move(temporary, path, overwrite: true);
    }
}
