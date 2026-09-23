using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.Search;

public sealed record ApplicationEntry(string Id, string Name, string LaunchPath, string? FilePath = null,
    string Arguments = "", bool IsFilesMate = false)
{
    public string Identity => IsFilesMate ? "filesmate" : !string.IsNullOrWhiteSpace(FilePath)
        ? FilePath.ToUpperInvariant() + "\n" + Arguments : Id.ToUpperInvariant();
    // A launcher entry may point at a shared browser executable. File actions
    // belong to its shortcut, never to that launch target.
    public string? ShortcutPath => Path.IsPathFullyQualified(LaunchPath) && Path.GetExtension(LaunchPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? LaunchPath : null;
    public string? LocationPath => ShortcutPath ?? (IsFilesMate ? FilePath : null);
    public string SearchText => Name + " " + (FilePath is null ? Id : Path.GetFileNameWithoutExtension(FilePath));
    public NameHit ToHit() => new(Name, LaunchPath, false, this);
}

public interface IApplicationCatalog
{
    public Task<IReadOnlyList<ApplicationEntry>> GetAsync(CancellationToken token);
}

public static class ApplicationCatalog
{
    // Keep each result kind in its own rank band so relevance never crosses
    // the user-configured order of registered apps and indexed executables.
    public const int RankStride = 8;

    public static IReadOnlyList<ApplicationEntry> Normalize(IEnumerable<ApplicationEntry> entries) => entries
        .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.LaunchPath) && !Maintenance(entry))
        .OrderByDescending(entry => entry.IsFilesMate)
        .ThenByDescending(entry => entry.ShortcutPath is not null)
        .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
        .DistinctBy(entry => entry.Identity, StringComparer.OrdinalIgnoreCase)
        .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static bool Maintenance(ApplicationEntry entry)
    {
        var name = entry.Name.Trim();
        var file = Path.GetFileNameWithoutExtension(entry.FilePath ?? "");
        return name.StartsWith("卸载", StringComparison.Ordinal) || name.StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase)
            || file.Equals("uninstall", StringComparison.OrdinalIgnoreCase)
            || file.StartsWith("unins0", StringComparison.OrdinalIgnoreCase);
    }

    public static SearchHitKind FileKind(string path, bool directory)
    {
        return SearchHitRanking.Classify(path, directory, recognizeApplicationShortcuts: false);
    }

    public static bool IsIndexedExecutable(string path, bool directory) => !directory &&
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".com";

    public static Func<string, bool, int> FileRank(IEnumerable<SearchHitKind> order, string query)
    {
        var priorities = SearchHitKinds.SanitizeOrder(order).Select((kind, index) => (kind, index)).ToDictionary(pair => pair.kind, pair => pair.index);
        return (path, directory) =>
        {
            var kind = FileKind(path, directory);
            return priorities[kind] * RankStride + NameIndexReader.Relevance(Path.GetFileName(path), query);
        };
    }
}

/// <summary>Merge application entities and files before pagination, honoring the shared type order.</summary>
public sealed class LauncherSearchProvider(IApplicationCatalog applications, string? profile = null,
    IGlobalSearchProvider? files = null) : IGlobalSearchProvider
{
    private readonly IGlobalSearchProvider _files = files ?? new IndexedFilesSearchProvider(profile, launcherFiles: true);

    public async Task<GlobalSearchResponse> SearchAsync(string query, CancellationToken cancellationToken,
        SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query)) return new([], false);
        if (filter is not (SearchFilter.All or SearchFilter.Apps))
            return await _files.SearchAsync(query, cancellationToken, filter, limit, offset).ConfigureAwait(false);
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        var terms = NameIndexReader.Terms(query);
        var hidden = HiddenSearchResults.Load(profile);
        var catalog = await applications.GetAsync(cancellationToken).ConfigureAwait(false);
        var appHits = ApplicationCatalog.Normalize(catalog).Where(entry => !HiddenSearchResults.Contains(hidden, entry.ToHit()) && NameIndexReader.Matches(entry.SearchText, terms))
            .Select(entry => entry.ToHit()).ToArray();
        var order = SearchRankingConfiguration.Load(profile);
        var appRank = order.ToList().IndexOf(SearchHitKind.Program) * ApplicationCatalog.RankStride;
        var fileRank = ApplicationCatalog.FileRank(order, query);
        int ApplicationRelevance(NameHit hit) => NameIndexReader.Matches(hit.Name, terms)
            ? NameIndexReader.Relevance(hit.Name, query)
            : 4 + NameIndexReader.Relevance(Path.GetFileNameWithoutExtension(hit.Application!.FilePath ?? hit.Name), query);
        IEnumerable<NameHit> Sort(IEnumerable<NameHit> hits) => hits
            .OrderBy(hit => hit.Application is null ? fileRank(hit.Path, hit.IsDirectory) : appRank + ApplicationRelevance(hit))
            .ThenBy(hit => hit.Name.Length).ThenBy(hit => hit.Name, StringComparer.OrdinalIgnoreCase).ThenBy(hit => hit.Path, StringComparer.Ordinal);
        if (filter == SearchFilter.Apps)
        {
            var apps = Sort(appHits).Skip(offset).Take(limit + 1).ToArray();
            return new(apps.Take(limit).ToArray(), apps.Length > limit);
        }
        // At most all matching apps can precede the requested file offset.
        // Keep that overlap so both default and custom group orders page correctly.
        var fileOffset = Math.Max(0, offset - appHits.Length);
        GlobalSearchResponse response;
        try { response = await _files.SearchAsync(query, cancellationToken, filter, limit + appHits.Length + 1, fileOffset).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException)
        { response = new([], false, "文件索引暂时不可用，仍可搜索应用。"); }
        var combined = Sort(appHits.Concat(response.Hits)).Skip(offset - fileOffset).Take(limit + 1).ToArray();
        return new(combined.Take(limit).ToArray(), combined.Length > limit || response.HasMore, response.Notice);
    }
}
