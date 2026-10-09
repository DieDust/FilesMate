using System.Text.Json;

using FilesMate.App.Navigation;

namespace FilesMate.App.Services;

/// <summary>
/// Tracks recently opened folders for the taskbar jump list, like Explorer's Frequent list.
/// </summary>
public sealed class RecentFolderStore
{
    public const int MaxEntries = 10;
    private readonly Action<string, string> _commit;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public RecentFolderStore(string filePath)
        : this(filePath, Commit)
    {
    }

    internal RecentFolderStore(string filePath, Action<string, string> commit)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Recent folders path is required.", nameof(filePath));
        }

        FilePath = filePath;
        _commit = commit;
    }

    public string FilePath { get; }

    public static string DefaultFilePath =>
        Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
            "recent-folders.json");

    public IReadOnlyList<string> Load()
    {
        try
        {
            return Read();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Unlike Load's legacy best-effort API, the background owner must distinguish
    // an unreadable existing file from empty history before replacing it.
    internal IReadOnlyList<string> Read()
    {
        try
        {
            var paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            return Normalize(paths);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
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

    internal void Save(IReadOnlyList<string> paths)
    {
        var json = JsonSerializer.Serialize(paths, JsonOptions);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Each writer owns its staging file, including separate app instances.
        // Never delete the destination or fall back to an in-place partial write.
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                writer.Write(json);
            }

            _commit(temp, FilePath);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Trace.TraceError("Recent folder staging cleanup failed: {0}", error);
            }
        }
    }

    private static void Commit(string temp, string destination)
    {
        if (File.Exists(destination))
        {
            File.Replace(temp, destination, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, destination);
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

    internal static string[] Normalize(IEnumerable<string> paths) =>
        paths
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntries)
            .ToArray();
}
