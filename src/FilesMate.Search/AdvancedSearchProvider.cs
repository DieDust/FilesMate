using System.Diagnostics;
using System.Runtime.CompilerServices;
using FilesMate.App.Models;

namespace FilesMate.Search;

/// <summary>One ordering for registered applications and indexed files, before pagination.</summary>
public sealed class AdvancedSearchProvider(IApplicationCatalog catalog, string? profile = null)
{
    internal delegate Task<AdvancedSearchResponse> IndexedFileSource(string database, SearchPageRequest request,
        AdvancedSearchQuery query, int limit, int offset, CancellationToken token,
        Func<string, bool, bool> include, Func<string, bool, int> rank, bool includeTotal);
    private readonly IndexedFileSource? _indexedFileSource;

    internal AdvancedSearchProvider(IApplicationCatalog catalog, string? profile, IndexedFileSource indexedFileSource)
        : this(catalog, profile) => _indexedFileSource = indexedFileSource;

    public async Task<AdvancedSearchResponse> SearchAsync(string database, SearchPageRequest request, int offset,
        CancellationToken token, int limit = 200, bool includeTotal = true)
    {
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        var plan = CreatePlan(request);
        var apps = await ReadApplicationsAsync(plan, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var fileOffset = Math.Max(0, offset - apps.Length);
        var files = await ReadFilesAsync(database, plan, limit + apps.Length + 1, fileOffset, token,
            includeTotal && offset == 0).ConfigureAwait(false);
        return Merge(plan, apps, files, limit, offset - fileOffset, includeTotal && offset == 0);
    }

    internal async IAsyncEnumerable<AdvancedSearchUpdate> SearchUpdatesAsync(string database, SearchPageRequest request,
        [EnumeratorCancellation] CancellationToken token, int limit = 40)
    {
        var started = Stopwatch.GetTimestamp();
        token.ThrowIfCancellationRequested();
        using var sourceLifetime = new SourceLifetime(token);
        var sourceToken = sourceLifetime.Token;
        limit = Math.Clamp(limit, 1, 200);
        var plan = CreatePlan(request);
        long? appsStarted = plan.IncludeApplications ? Stopwatch.GetTimestamp() : null;
        var applications = plan.IncludeApplications
            ? CaptureAsync(() => ReadApplicationsAsync(plan, sourceToken), Array.Empty<AdvancedSearchHit>(), appsStarted!.Value)
            : null;
        long? filesStarted = plan.IncludeFiles ? Stopwatch.GetTimestamp() : null;
        // A first-page merge needs at most limit+1 candidates from either source.
        // It does not depend on the eventual number of matching applications.
        var indexedFiles = plan.IncludeFiles
            ? CaptureAsync(() => ReadFilesAsync(database, plan, limit + 1, 0, sourceToken, false), new AdvancedSearchResponse([], false), filesStarted!.Value)
            : null;
        AdvancedSearchHit[] apps = [];
        AdvancedSearchResponse files = new([], false);
        long? appsCompleted = null, filesCompleted = null;
        var incomplete = false;
        while (applications is not null || indexedFiles is not null)
        {
            Task next = applications is null ? indexedFiles! : indexedFiles is null ? applications
                : await Task.WhenAny(applications, indexedFiles).WaitAsync(token).ConfigureAwait(false);
            await next.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (applications?.IsCompleted == true)
            {
                var result = await applications.ConfigureAwait(false);
                apps = result.Value;
                appsCompleted = result.CompletedTimestamp;
                incomplete |= result.Failed;
                applications = null;
            }
            if (indexedFiles?.IsCompleted == true)
            {
                var result = await indexedFiles.ConfigureAwait(false);
                files = result.Value;
                filesCompleted = result.CompletedTimestamp;
                incomplete |= result.Failed;
                indexedFiles = null;
            }
            token.ThrowIfCancellationRequested();
            var final = applications is null && indexedFiles is null;
            var response = Merge(plan, apps, files, limit, 0, false);
            if (incomplete) response = response with { Notice = "Search_PartialUnavailable", HasMore = false };
            // Paging cannot start until every required source has completed successfully.
            if (!final) response = response with { HasMore = false };
            yield return new(response, final,
                new(started, appsStarted, appsCompleted, filesStarted, filesCompleted), incomplete);
        }
    }

    private sealed record SourceResult<T>(T Value, bool Failed, long CompletedTimestamp);

    private sealed class SourceLifetime(CancellationToken token) : IDisposable
    {
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(token);
        public CancellationToken Token => _source.Token;
        public void Dispose() { _source.Cancel(); _source.Dispose(); }
    }

    private static Task<SourceResult<T>> CaptureAsync<T>(Func<Task<T>> read, T empty, long started)
        => Task.Run(async () =>
        {
            try { return new SourceResult<T>(await read().ConfigureAwait(false), false, Stopwatch.GetTimestamp()); }
            catch (Exception error)
            {
                // Keep the other source usable. Query cancellation is checked by the
                // consumer before any update, including providers that ignore it.
                if (error is not OperationCanceledException)
                    Trace.WriteLine($"FilesMate search source failed after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms: {error}");
                return new SourceResult<T>(empty, true, Stopwatch.GetTimestamp());
            }
        });

    private sealed record SearchPlan(SearchPageRequest Request, AdvancedSearchQuery Query,
        bool IncludeApplications, bool IncludeFiles, Func<string, bool, bool> Include,
        Func<ApplicationEntry, bool> IncludeApplication, Func<string, bool, int> Rank, int ApplicationRank);

    private SearchPlan CreatePlan(SearchPageRequest request)
    {
        var query = AdvancedSearchQuery.Parse(request);
        var categories = SearchCategories.Load(profile ?? FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath);
        var category = categories.FirstOrDefault(c => c.Id == request.CategoryId) ?? SearchCategories.Defaults()[0];
        var hidden = HiddenSearchResults.Load(profile);
        var enabled = SearchExecutableConfiguration.Load(profile);
        var order = SearchRankingConfiguration.Load(profile);
        var rank = ApplicationCatalog.FileRank(order, request.Query);
        bool Include(string path, bool directory)
        {
            if (!enabled && ApplicationCatalog.IsIndexedExecutable(path, directory)) return false;
            if (hidden.Count > 0 && HiddenSearchResults.Contains(hidden, new(path, path, directory))) return false;
            if (category.Builtin is null) return !directory && category.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            // Registered applications come from the catalog; arbitrary shortcuts
            // and executables retain their separate user-configurable ranks.
            return category.Builtin != SearchFilter.Apps && SearchFilters.Matches(category.Builtin.Value, path, directory);
        }
        var appRank = order.ToList().IndexOf(SearchHitKind.Program) * ApplicationCatalog.RankStride;
        return new(request, query, category.Builtin is SearchFilter.All or SearchFilter.Apps, category.Builtin != SearchFilter.Apps,
            Include, a => hidden.Count == 0 || !HiddenSearchResults.Contains(hidden, a.ToHit()), rank, appRank);
    }

    private async Task<AdvancedSearchHit[]> ReadApplicationsAsync(SearchPlan plan, CancellationToken token)
    {
        if (!plan.IncludeApplications) return [];
        var entries = await catalog.GetAsync(token).WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return ApplicationCatalog.Normalize(entries).Where(plan.IncludeApplication)
            .Select(a => new AdvancedSearchHit(a.Name, a.LaunchPath, false, Application: a))
            .Where(plan.Query.Matches).ToArray();
    }

    private Task<AdvancedSearchResponse> ReadFilesAsync(string database, SearchPlan plan, int limit, int offset,
        CancellationToken token, bool includeTotal)
    {
        if (!plan.IncludeFiles) return Task.FromResult(new AdvancedSearchResponse([], false));
        if (_indexedFileSource is not null)
            return _indexedFileSource(database, plan.Request, plan.Query, limit, offset, token, plan.Include, plan.Rank, includeTotal);
        return File.Exists(database)
            ? Task.Run(() => NameIndexReader.SearchAdvanced(database, plan.Request, plan.Query, limit, offset, token,
                plan.Include, plan.Rank, includeTotal), token)
            : Task.FromResult(new AdvancedSearchResponse([], false, "SearchPage_IndexMissing"));
    }

    private static AdvancedSearchResponse Merge(SearchPlan plan, AdvancedSearchHit[] apps, AdvancedSearchResponse files,
        int limit, int skip, bool includeTotal)
    {
        IOrderedEnumerable<AdvancedSearchHit> Sort(IEnumerable<AdvancedSearchHit> hits) => plan.Request.Sort switch
        {
            SearchResultSort.Name => hits.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.NameDescending => hits.OrderByDescending(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.SizeDescending => hits.OrderByDescending(h => h.Size).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.ModifiedDescending => hits.OrderByDescending(h => h.ModifiedUtcTicks).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
            SearchResultSort.Path => hits.OrderBy(h => h.Path, StringComparer.OrdinalIgnoreCase),
            _ => hits.OrderBy(h => h.Application is null ? plan.Rank(h.Path, h.IsDirectory) : plan.ApplicationRank + ApplicationCatalog.Relevance(h.Application, plan.Request.Query))
                .ThenBy(h => h.Name.Length).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase),
        };
        var combined = Sort(apps.Concat(files.Hits)).ThenBy(h => h.Path, StringComparer.Ordinal)
            .Skip(skip).Take(limit + 1).ToArray();
        return new(combined.Take(limit).ToArray(), combined.Length > limit || files.HasMore, files.Notice,
            includeTotal ? (files.TotalCount ?? 0) + apps.Length : null);
    }
}
