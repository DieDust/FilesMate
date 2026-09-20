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
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json")));
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("rankOrder", out var order) && order.ValueKind == JsonValueKind.Array)
                return SearchHitKinds.FromSavedOrder(order.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => Enum.TryParse<SearchHitKind>(v.GetString(), true, out var kind) ? kind : (SearchHitKind)(-1)),
                    json.RootElement.TryGetProperty("rankVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) ? number : 0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return SearchHitKinds.DefaultOrder;
    }

    public static void Save(IEnumerable<SearchHitKind> order, string? profile = null)
    {
        var path = Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json");
        // Read-modify-write only rankOrder, preserving disk roots, exclusions,
        // database location and future fields owned by the file manager.
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new JsonException("索引设置格式无效。") : new JsonObject();
        document["rankOrder"] = new JsonArray(SearchHitKinds.SanitizeOrder(order).Select(kind => JsonValue.Create(kind.ToString()) as JsonNode).ToArray());
        document["rankVersion"] = 3;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
