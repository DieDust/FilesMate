using System.Text.Json;

using FilesMate.App.Navigation;

namespace FilesMate.App.Services;

/// <summary>
/// Tracks recently opened folders for the taskbar jump list, like Explorer's Frequent list.
/// </summary>
public sealed class RecentFolderStore
{
    public const int MaxEntries = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public RecentFolderStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Recent folders path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FilesMate",
            "recent-folders.json");

    public IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            var paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            return Normalize(paths);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> Record(string? path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
        {
            return Load();
        }

        var next = Load()
            .Where(item => !string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
            .Prepend(normalized)
            .Take(MaxEntries)
            .ToArray();
        Save(next);
        return next;
    }

    private void Save(IReadOnlyList<string> paths)
    {
        var json = JsonSerializer.Serialize(paths, JsonOptions);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || HomeLocation.IsHome(path)
            || TagLocation.IsTag(path))
        {
            return null;
        }

        return PinnedLocationStore.Normalize(path);
    }

    private static string[] Normalize(IEnumerable<string> paths) =>
        paths
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntries)
            .ToArray();
}
