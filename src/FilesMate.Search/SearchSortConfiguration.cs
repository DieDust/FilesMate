using System.Text.Json.Nodes;

namespace FilesMate.Search;

/// <summary>A single persisted result order for search tabs and the global palette.</summary>
public static class SearchSortConfiguration
{
    public static SearchResultSort Load(string? profile = null)
    {
        try
        {
            var document = SearchIndexConfigurationFile.Read(Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json"));
            var value = SearchIndexConfigurationFile.GetProperty(document, "resultSort")?.ToString();
            return Enum.TryParse<SearchResultSort>(value, out var sort) && Enum.IsDefined(sort) ? sort : SearchResultSort.Priority;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return SearchResultSort.Priority; }
    }

    public static void Save(SearchResultSort sort, string? profile = null)
    {
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        SearchIndexConfigurationFile.Update(Path.Combine(profile ?? GlobalSearchConfiguration.DefaultDirectory, "search-index.json"),
            document => SearchIndexConfigurationFile.SetProperty(document, "resultSort", JsonValue.Create(sort.ToString())));
    }
}
