using System.Text.Json;

using FilesMate.App.Models;

namespace FilesMate.App.Services;

public sealed class SearchIndexSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public SearchIndexSettingsService(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Settings path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesMate",
        "search-index.json");

    public SearchIndexSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return SearchIndexSettings.Default;
            }

            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath), JsonOptions);
            return dto is null
                ? SearchIndexSettings.Default
                : SearchIndexSettings.Sanitize(
                    dto.Roots,
                    dto.Exclusions,
                    dto.ScanInParallel,
                    dto.MaxDepth,
                    dto.DatabaseDirectory,
                    dto.AutoRefresh,
                    SearchHitKinds.FromSavedOrder(ParseRankOrder(dto.RankOrder), dto.RankVersion));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return SearchIndexSettings.Default;
        }
    }

    public async Task SaveAsync(SearchIndexSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(
            new Dto
            {
                Roots = [.. settings.Roots],
                Exclusions = [.. settings.Exclusions],
                ScanInParallel = settings.ScanInParallel,
                MaxDepth = settings.MaxDepth,
                DatabaseDirectory = settings.DatabaseDirectory,
                AutoRefresh = settings.AutoRefresh,
                RankOrder = [.. settings.RankOrder.Select(kind => kind.ToString())],
                RankVersion = 3,
            },
            JsonOptions);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        if (File.Exists(FilePath))
        {
            File.Replace(temp, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, FilePath);
        }
    }

    private sealed class Dto
    {
        public string[]? Roots { get; set; }

        public string[]? Exclusions { get; set; }

        public bool? ScanInParallel { get; set; }

        public int? MaxDepth { get; set; }

        public string? DatabaseDirectory { get; set; }

        public bool? AutoRefresh { get; set; }

        public string[]? RankOrder { get; set; }
        public int RankVersion { get; set; }
    }

    private static IReadOnlyList<SearchHitKind> ParseRankOrder(string[]? names)
    {
        if (names is null || names.Length == 0)
        {
            return SearchHitKinds.DefaultOrder;
        }

        var kinds = new List<SearchHitKind>();
        foreach (var name in names)
        {
            if (Enum.TryParse(name, ignoreCase: true, out SearchHitKind kind))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }
}
