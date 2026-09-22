using System.Text.Json;
using System.Text.Json.Nodes;
using FilesMate.App.Models;

namespace FilesMate.Search;

/// <summary>Both search surfaces share the existing index configuration.</summary>
public static class SearchRankingConfiguration
{
    public static IReadOnlyList<SearchHitKind> Load(string? profile = null)
    {
        try
        {
            var document = SearchIndexConfigurationFile.Read(Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json"));
            if (SearchIndexConfigurationFile.GetProperty(document, "rankOrder") is JsonArray order)
                return SearchHitKinds.FromSavedOrder(order.Where(value => value?.GetValueKind() == JsonValueKind.String)
                    .Select(value => Enum.TryParse<SearchHitKind>(value!.GetValue<string>(), true, out var kind) ? kind : (SearchHitKind)(-1)),
                    SearchIndexConfigurationFile.GetProperty(document, "rankVersion") is JsonValue version && version.TryGetValue<int>(out var number) ? number : 0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return SearchHitKinds.DefaultOrder;
    }

    public static void Save(IEnumerable<SearchHitKind> order, string? profile = null)
    {
        var path = Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json");
        var sanitized = SearchHitKinds.SanitizeOrder(order);
        SearchIndexConfigurationFile.Update(path, document =>
        {
            SearchIndexConfigurationFile.SetProperty(document, "rankOrder",
                new JsonArray(sanitized.Select(kind => JsonValue.Create(kind.ToString()) as JsonNode).ToArray()));
            SearchIndexConfigurationFile.SetProperty(document, "rankVersion", JsonValue.Create(3));
        });
    }
}
