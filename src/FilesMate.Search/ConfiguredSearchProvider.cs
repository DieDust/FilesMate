using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FilesMate.Search;

/// <summary>The palette uses the same predicates and global ordering as a full search tab.</summary>
public sealed class ConfiguredSearchProvider(IApplicationCatalog catalog, string profile, string? categoryId = null) : IProgressiveGlobalSearchProvider
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

    public async IAsyncEnumerable<GlobalSearchUpdate> SearchUpdatesAsync(string query,
        [EnumeratorCancellation] CancellationToken cancellationToken, SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0)
    {
        var started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query))
        {
            yield return new(new([], false), true, new(started));
            yield break;
        }
        if (offset > 0)
        {
            var response = await SearchAsync(query, cancellationToken, filter, limit, offset).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(response, true, new(started));
            yield break;
        }
        var request = new SearchPageRequest(query, CategoryId: categoryId ?? filter.ToString(), Sort: SearchSortConfiguration.Load(profile));
        await foreach (var update in new AdvancedSearchProvider(catalog, profile).SearchUpdatesAsync(
            GlobalSearchConfiguration.ResolveDatabase(profile), request, cancellationToken, limit).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(new(update.Response.Hits.Select(h => new NameHit(h.Name, h.Path, h.IsDirectory, h.Application)).ToArray(),
                update.Response.HasMore, update.Response.Notice), update.IsFinal, update.Timings with { StartedTimestamp = started }, update.IsIncomplete);
        }
    }
}
