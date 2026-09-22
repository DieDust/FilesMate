using System.Text.Json;
using System.Text.Json.Nodes;

using FilesMate.App.Models;
using FilesMate.Search;

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

        FilePath = Path.GetFullPath(filePath);
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

            var dto = SearchIndexConfigurationFile.Read(FilePath).Deserialize<Dto>(JsonOptions);
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

    public Task SaveAsync(SearchIndexSettings settings, CancellationToken cancellationToken = default,
        bool updateDatabaseDirectory = false, bool updateRankOrder = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var changes = JsonSerializer.SerializeToNode(
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
            JsonOptions)!.AsObject();
        return Task.Run(() => SearchIndexConfigurationFile.Update(FilePath, document =>
        {
            // Never turn a failed load into a write that discards malformed existing settings.
            _ = document.Deserialize<Dto>(JsonOptions);
            foreach (var name in new[] { "roots", "exclusions", "scanInParallel", "maxDepth", "autoRefresh" })
                SearchIndexConfigurationFile.SetProperty(document, name, changes[name]?.DeepClone());
            // A settings page may predate a successful relocation or a host-side ranking change.
            // Update these shared fields only for an explicit edit or their first initialization.
            if (updateDatabaseDirectory || !SearchIndexConfigurationFile.HasProperty(document, "databaseDirectory"))
                SearchIndexConfigurationFile.SetProperty(document, "databaseDirectory", changes["databaseDirectory"]?.DeepClone());
            if (updateRankOrder || !SearchIndexConfigurationFile.HasProperty(document, "rankOrder"))
            {
                SearchIndexConfigurationFile.SetProperty(document, "rankOrder", changes["rankOrder"]?.DeepClone());
                SearchIndexConfigurationFile.SetProperty(document, "rankVersion", JsonValue.Create(3));
            }
        }, cancellationToken), cancellationToken);
    }

    /// <summary>Publishes a successfully prepared index location without overwriting other settings.</summary>
    public Task UpdateDatabaseDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("Index directory must be an absolute path.", nameof(directory));
        var fullPath = Path.GetFullPath(directory);
        return Task.Run(() => SearchIndexConfigurationFile.Update(FilePath, document =>
        {
            _ = document.Deserialize<Dto>(JsonOptions);
            SearchIndexConfigurationFile.SetProperty(document, "databaseDirectory", JsonValue.Create(fullPath));
        }, cancellationToken), cancellationToken);
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
