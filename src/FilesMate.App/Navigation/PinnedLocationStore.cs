using System.Text.Json;

namespace FilesMate.App.Navigation;

public sealed record PinnedLocationState(
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> HiddenDefaultIds,
    IReadOnlyList<string> CloudPaths,
    IReadOnlyList<string>? ItemOrder = null,
    IReadOnlyList<string>? CloudOrder = null,
    IReadOnlyList<string>? DriveOrder = null,
    IReadOnlyList<string>? SectionOrder = null,
    IReadOnlyList<string>? HiddenCloudPaths = null,
    IReadOnlyList<CloudLocationOverride>? CloudOverrides = null);

public sealed record CloudLocationOverride(string Id, string Label, string Target, string? OriginalTarget = null);

/// <summary>
/// Persists custom locations together with default sidebar entries the user
/// chose to hide. Legacy files containing only a JSON path array remain valid.
/// </summary>
public sealed class PinnedLocationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public PinnedLocationStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Pinned locations path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FilesMate",
            "pinned-locations.json");

    public IReadOnlyList<string> Load() => LoadState().Paths;

    public PinnedLocationState LoadState()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return EmptyState();
            }

            var json = File.ReadAllText(FilePath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacyPaths = JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
                return new PinnedLocationState(NormalizeDistinct(legacyPaths), [], []);
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return EmptyState();
            }

            var stored = JsonSerializer.Deserialize<PinnedLocationFile>(json, JsonOptions);
            return stored is null
                ? EmptyState()
                : new PinnedLocationState(
                    NormalizeDistinct(stored.Paths ?? []),
                    NormalizeIds(stored.HiddenDefaultIds ?? []),
                    NormalizeDistinct(stored.CloudPaths ?? []),
                    NormalizeIds(stored.ItemOrder ?? []),
                    NormalizeIds(stored.CloudOrder ?? []),
                    NormalizeIds(stored.DriveOrder ?? []),
                    NormalizeIds(stored.SectionOrder ?? []),
                    NormalizeDistinct(stored.HiddenCloudPaths ?? []),
                    NormalizeCloudOverrides(stored.CloudOverrides ?? []));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return EmptyState();
        }
    }

    public bool IsPinned(string? path)
    {
        var normalized = Normalize(path);
        return normalized is not null
            && Load().Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsDefaultHidden(string id) =>
        LoadState().HiddenDefaultIds.Contains(NormalizeId(id), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Add(string path)
    {
        var normalized = Normalize(path)
            ?? throw new ArgumentException("A valid folder path is required.", nameof(path));
        var state = LoadState();
        var paths = NormalizeDistinct(state.Paths.Append(normalized));
        SaveState(state with { Paths = paths });
        return paths;
    }

    public IReadOnlyList<string> Remove(string path)
    {
        var normalized = Normalize(path);
        if (normalized is null)
        {
            return Load();
        }

        var state = LoadState();
        var paths = state.Paths
            .Where(item => !string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SaveState(state with { Paths = paths });
        return paths;
    }

    public void HideDefault(string id)
    {
        var normalizedId = NormalizeId(id);
        if (normalizedId.Length == 0)
        {
            throw new ArgumentException("A default location id is required.", nameof(id));
        }

        var state = LoadState();
        SaveState(state with
        {
            HiddenDefaultIds = NormalizeIds(state.HiddenDefaultIds.Append(normalizedId)),
        });
    }

    public void ShowDefault(string id)
    {
        var normalizedId = NormalizeId(id);
        var state = LoadState();
        SaveState(state with
        {
            HiddenDefaultIds = state.HiddenDefaultIds
                .Where(item => !string.Equals(item, normalizedId, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        });
    }

    public void RestoreDefaults()
    {
        var state = LoadState();
        SaveState(state with { HiddenDefaultIds = [] });
    }

    public void Save(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var state = LoadState();
        SaveState(state with { Paths = NormalizeDistinct(paths) });
    }

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);
            if (!string.IsNullOrEmpty(root)
                && string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var a = left.Trim().TrimEnd('\\', '/');
        var b = right.Trim().TrimEnd('\\', '/');
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HomeLocation.IsHome(left)
            || HomeLocation.IsHome(right)
            || TagLocation.IsTag(left)
            || TagLocation.IsTag(right))
        {
            return false;
        }

        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private void SaveState(PinnedLocationState state)
    {
        var stored = new PinnedLocationFile
        {
            Paths = NormalizeDistinct(state.Paths),
            HiddenDefaultIds = NormalizeIds(state.HiddenDefaultIds),
            CloudPaths = NormalizeDistinct(state.CloudPaths),
            ItemOrder = NormalizeIds(state.ItemOrder ?? []),
            CloudOrder = NormalizeIds(state.CloudOrder ?? []),
            DriveOrder = NormalizeIds(state.DriveOrder ?? []),
            SectionOrder = NormalizeIds(state.SectionOrder ?? []),
            HiddenCloudPaths = NormalizeDistinct(state.HiddenCloudPaths ?? []),
            CloudOverrides = NormalizeCloudOverrides(state.CloudOverrides ?? []),
        };
        var json = JsonSerializer.Serialize(stored, JsonOptions);
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

    public IReadOnlyList<string> AddCloud(string path)
    {
        var normalized = Normalize(path)
            ?? throw new ArgumentException("A valid folder path is required.", nameof(path));
        var state = LoadState();
        SaveState(state with
        {
            CloudPaths = NormalizeDistinct(state.CloudPaths.Append(normalized)),
            HiddenCloudPaths = (state.HiddenCloudPaths ?? []).Where(item => !PathsEqual(item, normalized)).ToArray(),
        });
        return LoadState().CloudPaths;
    }

    public IReadOnlyList<string> RemoveCloud(string path)
    {
        var normalized = Normalize(path);
        var state = LoadState();
        if (normalized is null)
        {
            return state.CloudPaths;
        }

        SaveState(state with
        {
            CloudPaths = state.CloudPaths
                .Where(item => !string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        });
        return LoadState().CloudPaths;
    }

    public void EditCloud(string id, string label, string target, string? originalTarget = null)
    {
        if (!id.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(label) || label.Trim().Length > 120
            || !Path.IsPathFullyQualified(target) || Normalize(target) is not { } normalized)
            throw new ArgumentException("A cloud entry name and absolute folder path are required.");
        var state = LoadState();
        SaveState(state with
        {
            CloudOverrides = (state.CloudOverrides ?? [])
                .Where(item => !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                .Append(new CloudLocationOverride(id, label.Trim(), normalized,
                    (state.CloudOverrides ?? []).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))?.OriginalTarget
                        ?? Normalize(originalTarget))).ToArray(),
            HiddenCloudPaths = (state.HiddenCloudPaths ?? []).Where(item => !PathsEqual(item, normalized)).ToArray(),
        });
    }

    public void HideCloud(string path)
    {
        var normalized = Normalize(path) ?? throw new ArgumentException("A valid folder path is required.", nameof(path));
        var state = LoadState();
        // Keep the reference and its name so adding the same folder restores it.
        // Filtering by path also suppresses aliases of auto-discovered accounts.
        SaveState(state with { HiddenCloudPaths = NormalizeDistinct((state.HiddenCloudPaths ?? []).Append(normalized)) });
    }

    private static CloudLocationOverride[] NormalizeCloudOverrides(IEnumerable<CloudLocationOverride> entries) =>
        entries.Where(item => item is not null && item.Id?.StartsWith("cloud:", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrWhiteSpace(item.Label) && item.Label.Trim().Length <= 120
                && item.Target is not null && Path.IsPathFullyQualified(item.Target) && Normalize(item.Target) is not null)
            .DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(item => new CloudLocationOverride(item.Id, item.Label.Trim(), Normalize(item.Target)!, Normalize(item.OriginalTarget))).ToArray();

    public void SaveItemOrder(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var state = LoadState();
        SaveState(state with { ItemOrder = NormalizeIds(ids) });
    }

    public void SaveCloudOrder(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var state = LoadState();
        SaveState(state with { CloudOrder = NormalizeIds(ids) });
    }

    public void SaveDriveOrder(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var state = LoadState();
        SaveState(state with { DriveOrder = NormalizeIds(ids) });
    }

    public void SaveSectionOrder(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var state = LoadState();
        SaveState(state with { SectionOrder = NormalizeIds(ids) });
    }

    public static List<T> OrderByIds<T>(IEnumerable<T> items, IReadOnlyList<string>? order, Func<T, string> id)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(id);
        var remaining = items.ToList();
        if (order is null || order.Count == 0)
        {
            return remaining;
        }

        var result = new List<T>(remaining.Count);
        foreach (var key in order)
        {
            var index = remaining.FindIndex(item =>
                string.Equals(id(item), key, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            result.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        result.AddRange(remaining);
        return result;
    }

    public bool IsCloud(string? path)
    {
        var normalized = Normalize(path);
        return normalized is not null
            && LoadState().CloudPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static PinnedLocationState EmptyState() => new([], [], []);

    private static string[] NormalizeDistinct(IEnumerable<string> paths) =>
        paths
            .Select(Normalize)
            .Where(path => path is not null)
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] NormalizeIds(IEnumerable<string> ids) =>
        ids
            .Select(NormalizeId)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeId(string? id) => id?.Trim().ToLowerInvariant() ?? string.Empty;

    private sealed class PinnedLocationFile
    {
        public string[] Paths { get; init; } = [];

        public string[] HiddenDefaultIds { get; init; } = [];

        public string[] CloudPaths { get; init; } = [];

        public string[] ItemOrder { get; init; } = [];

        public string[] CloudOrder { get; init; } = [];

        public string[] DriveOrder { get; init; } = [];

        public string[] SectionOrder { get; init; } = [];

        public string[] HiddenCloudPaths { get; init; } = [];

        public CloudLocationOverride[] CloudOverrides { get; init; } = [];
    }
}
