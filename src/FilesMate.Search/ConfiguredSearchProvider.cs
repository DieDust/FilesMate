namespace FilesMate.Search;

/// <summary>The palette uses the same predicates and global ordering as a full search tab.</summary>
public sealed class ConfiguredSearchProvider(IApplicationCatalog catalog, string profile, string? categoryId = null) : IGlobalSearchProvider
{
    public ConfiguredSearchProvider ForCategory(string id) => new(catalog, profile, id);

    public async Task<GlobalSearchResponse> SearchAsync(string query, CancellationToken cancellationToken,
        SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query)) return new([], false);
        var request = new SearchPageRequest(query, CategoryId: categoryId ?? filter.ToString(), Sort: SearchSortConfiguration.Load(profile));
        var response = await new AdvancedSearchProvider(catalog, profile).SearchAsync(
            GlobalSearchConfiguration.ResolveDatabase(profile), request, offset, cancellationToken, limit, includeTotal: false).ConfigureAwait(false);
        return new(response.Hits.Select(h => new NameHit(h.Name, h.Path, h.IsDirectory, h.Application)).ToArray(), response.HasMore, response.Notice);
    }
}
