using System.Text.Json.Nodes;

namespace FilesMate.Search;

/// <summary>Controls whether indexed standalone executables appear in search results.</summary>
public static class SearchExecutableConfiguration
{
    public static bool Load(string? profile = null) => SearchRankingConfiguration.LoadPreferences(profile).IncludeStandaloneExecutables;

    public static void Save(bool enabled, string? profile = null)
    {
        var path = Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json");
        SearchIndexConfigurationFile.Update(path, document =>
            SearchIndexConfigurationFile.SetProperty(document, "includeStandaloneExecutables", JsonValue.Create(enabled)));
    }
}
