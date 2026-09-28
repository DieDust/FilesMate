using FilesMate.App.Models;

namespace FilesMate.Search;

/// <summary>One ordering for registered applications and indexed files, before pagination.</summary>
public sealed class AdvancedSearchProvider(IApplicationCatalog catalog, string? profile = null)
{
    public async Task<AdvancedSearchResponse> SearchAsync(string database, SearchPageRequest request, int offset,
        CancellationToken token, int limit = 200, bool includeTotal = true)
    {
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        var query = AdvancedSearchQuery.Parse(request);
        var categories = SearchCategories.Load(profile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesMate"));
        var category = categories.FirstOrDefault(c => c.Id == request.CategoryId) ?? SearchCategories.Defaults()[0];
        var hidden = HiddenSearchResults.Load(profile);
        var enabled = SearchExecutableConfiguration.Load(profile);
        var order = SearchRankingConfiguration.Load(profile);
        var rank = ApplicationCatalog.FileRank(order, request.Query);
        bool Include(string path, bool directory)
        {
            if (!enabled && ApplicationCatalog.IsIndexedExecutable(path, directory)) return false;
            if (HiddenSearchResults.Contains(hidden, new(path, path, directory))) return false;
            if (category.Builtin is null) return !directory && category.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            // Registered applications come from the catalog; arbitrary shortcuts
            // and executables retain their separate user-configurable ranks.
            return category.Builtin != SearchFilter.Apps && SearchFilters.Matches(category.Builtin.Value, path, directory);
        }
        AdvancedSearchHit[] apps = [];
        if (category.Builtin is SearchFilter.All or SearchFilter.Apps)
        {
            var entries = await catalog.GetAsync(token).ConfigureAwait(false);
            apps = ApplicationCatalog.Normalize(entries).Where(a => !HiddenSearchResults.Contains(hidden, a.ToHit()))
                .Select(a => new AdvancedSearchHit(a.Name, a.LaunchPath, false, Application: a))
                .Where(query.Matches).ToArray();
        }
        token.ThrowIfCancellationRequested();
        var fileOffset = Math.Max(0, offset - apps.Length);
        AdvancedSearchResponse files = new([], false);
        if (category.Builtin != SearchFilter.Apps)
            files = File.Exists(database)
                ? await Task.Run(() => NameIndexReader.SearchAdvanced(database, request, query, limit + apps.Length + 1, fileOffset, token, Include, rank, includeTotal && offset == 0), token).ConfigureAwait(false)
                : new([], false, "SearchPage_IndexMissing");
        var appRank = order.ToList().IndexOf(SearchHitKind.Program) * ApplicationCatalog.RankStride;
        IOrderedEnumerable<AdvancedSearchHit> Sort(IEnumerable<AdvancedSearchHit> hits) => request.Sort switch
        {
            SearchResultSort.Name => hits.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.NameDescending => hits.OrderByDescending(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.SizeDescending => hits.OrderByDescending(h => h.Size).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.ModifiedDescending => hits.OrderByDescending(h => h.ModifiedUtcTicks).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.Path => hits.OrderBy(h => h.Path, StringComparer.OrdinalIgnoreCase),
            _ => hits.OrderBy(h => h.Application is null ? rank(h.Path, h.IsDirectory) : appRank + ApplicationCatalog.Relevance(h.Application, request.Query))
                .ThenBy(h => h.Name.Length).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
        };
        var combined = Sort(apps.Concat(files.Hits)).ThenBy(h => h.Path, StringComparer.Ordinal)
            .Skip(offset - fileOffset).Take(limit + 1).ToArray();
        return new(combined.Take(limit).ToArray(), combined.Length > limit || files.HasMore, files.Notice,
            includeTotal && offset == 0 ? (files.TotalCount ?? 0) + apps.Length : null);
    }
}
