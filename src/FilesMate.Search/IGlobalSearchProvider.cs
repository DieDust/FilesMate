namespace FilesMate.Search;

public enum SearchFilter { All, Apps, Documents, Images, Media, Folders }

public static class SearchFilters
{
    public static bool Matches(SearchFilter filter, string path, bool directory)
    {
        var kind = FilesMate.App.Services.SearchHitRanking.Classify(path, directory);
        return filter switch
        {
            SearchFilter.Apps => kind is FilesMate.App.Models.SearchHitKind.Program,
            SearchFilter.Documents => kind == FilesMate.App.Models.SearchHitKind.Document,
            SearchFilter.Images => kind == FilesMate.App.Models.SearchHitKind.Image,
            SearchFilter.Media => kind is FilesMate.App.Models.SearchHitKind.Video or FilesMate.App.Models.SearchHitKind.Audio,
            SearchFilter.Folders => directory,
            _ => true,
        };
    }
}

public sealed record GlobalSearchResponse(IReadOnlyList<NameHit> Hits, bool HasMore, string? Notice = null);

/// <summary>Provider boundary for future search sources; no plugin code is loaded in this release.</summary>
public interface IGlobalSearchProvider
{
    public Task<GlobalSearchResponse> SearchAsync(string query, CancellationToken cancellationToken, SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0);
}

public sealed class IndexedFilesSearchProvider(string? profileDirectory = null, bool launcherFiles = false, IReadOnlyList<string>? extensions = null) : IGlobalSearchProvider
{
    public async Task<GlobalSearchResponse> SearchAsync(string query, CancellationToken cancellationToken, SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query)) return new([], false);
        var database = GlobalSearchConfiguration.ResolveDatabase(profileDirectory);
        if (!File.Exists(database)) return new([], false, "尚未建立索引。请在文件管理器的“设置 → 搜索”中选择磁盘并建立索引。");
        if (launcherFiles && filter == SearchFilter.Apps) return new([], false);
        limit = Math.Clamp(limit, 1, launcherFiles ? 10000 : 200);
        var hidden = HiddenSearchResults.Load(profileDirectory).Values
            .Select(item => item.Key.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? item.Key : Path.IsPathFullyQualified(item.Location) ? HiddenSearchResults.FileKey(item.Location) : item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Include(string path, bool directory) => !hidden.Contains(HiddenSearchResults.FileKey(path)) &&
            (extensions is not null ? !directory && extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) : SearchFilters.Matches(filter, path, directory));
        var hits = await Task.Run(() => NameIndexReader.Search(database, query, null, limit + 1, cancellationToken,
            launcherFiles ? ApplicationCatalog.FileRank(SearchRankingConfiguration.Load(profileDirectory), query) : FilesMate.App.Services.SearchHitRanking.CreateRank(SearchRankingConfiguration.Load(profileDirectory), query),
            Include, offset: Math.Max(0, offset), rankByPath: true), cancellationToken).ConfigureAwait(false);
        return new(hits.Take(limit).ToArray(), hits.Count > limit);
    }
}
