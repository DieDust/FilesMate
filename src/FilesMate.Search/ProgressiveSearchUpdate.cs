namespace FilesMate.Search;

/// <summary>Monotonic Stopwatch timestamps for one query, independent of UI scheduling.</summary>
public sealed record SearchSourceTimings(long StartedTimestamp,
    long? ApplicationsStartedTimestamp = null, long? ApplicationsCompletedTimestamp = null,
    long? FilesStartedTimestamp = null, long? FilesCompletedTimestamp = null);

/// <summary>A replacement snapshot of the first page. HasMore is authoritative only at a successful final update.</summary>
public sealed record GlobalSearchUpdate(GlobalSearchResponse Response, bool IsFinal,
    SearchSourceTimings Timings, bool IsIncomplete = false);

/// <summary>Optional first-page progress; ordinary providers and the terminal API remain compatible.</summary>
public interface IProgressiveGlobalSearchProvider : IGlobalSearchProvider
{
    public IAsyncEnumerable<GlobalSearchUpdate> SearchUpdatesAsync(string query, CancellationToken cancellationToken,
        SearchFilter filter = SearchFilter.All, int limit = 40, int offset = 0);
}

internal sealed record AdvancedSearchUpdate(AdvancedSearchResponse Response, bool IsFinal,
    SearchSourceTimings Timings, bool IsIncomplete);
