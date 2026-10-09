using System;
using System.Collections.Generic;
using System.Linq;

namespace FilesMate.SearchHost;

internal enum SearchPresentationState { Default, Searching, Results, EmptyOrError }

internal readonly record struct SearchMatchSpan(int Start, int Length, bool Matched);

/// <summary>Pure presentation decisions; no provider, dispatcher or shell side effects.</summary>
internal static class SearchPresentationPolicy
{
    internal static SearchPresentationState Resolve(string? query, bool pending, bool currentResults, int count)
        => string.IsNullOrWhiteSpace(query) ? SearchPresentationState.Default
            : currentResults && count > 0 ? SearchPresentationState.Results
            : pending ? SearchPresentationState.Searching : SearchPresentationState.EmptyOrError;

    internal static bool HasCurrentResults(long generation, long acceptedGeneration, bool pending, bool composing)
        => generation == acceptedGeneration && !pending && !composing;

    // Some legacy providers report a missing source through a notice rather
    // than their incomplete flag. It must never become a completed empty search.
    internal static bool IncompleteResponse(bool incomplete, string? notice) => incomplete || notice is
        "SearchPage_IndexMissing" or "SearchPage_MetadataRequired" or "Search_IndexUnavailable" or "Search_PartialUnavailable" or "Search_PartialTimedOut";

    // A terminal partial response remains a failure even when useful rows survive.
    internal static bool FailedCompletion(bool isFinal, bool isIncomplete) => isFinal && isIncomplete;
    internal static bool SuccessfulCompletion(bool isFinal, bool isIncomplete) => isFinal && !isIncomplete;

    internal static bool ShowCompleted(SearchPresentationState state, bool completed, bool loading, bool failed)
        => state == SearchPresentationState.Results && completed && !loading && !failed;

    // Only the first useful batch selects a default target. Later reorders must
    // retain identities, and removal of a selected target must not choose another.
    internal static IReadOnlyList<T> RetainSelection<T>(IReadOnlyList<T> desired,
        IReadOnlyCollection<T> selected, bool firstBatch, IEqualityComparer<T>? comparer = null)
    {
        if (firstBatch) return desired.Count == 0 ? Array.Empty<T>() : new[] { desired[0] };
        var identities = new HashSet<T>(selected, comparer);
        return desired.Where(identities.Contains).ToArray();
    }

    // Text remains literal. File names and queries are never parsed as XAML/HTML.
    // Bounds keep work proportional to a visible row even with a malformed query.
    internal static IReadOnlyList<SearchMatchSpan> MatchSpans(string? text, string? query)
    {
        text ??= string.Empty;
        if (text.Length == 0) return Array.Empty<SearchMatchSpan>();
        if (text.Length > 32768 || string.IsNullOrWhiteSpace(query) || query.Length > 2048)
            return new[] { new SearchMatchSpan(0, text.Length, false) };
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var matches = new bool[text.Length];
        var matchCount = 0;
        foreach (var word in words)
        {
            var position = 0;
            while (position < text.Length)
            {
                position = text.IndexOf(word, position, StringComparison.OrdinalIgnoreCase);
                if (position < 0) break;
                if (++matchCount > 64) return new[] { new SearchMatchSpan(0, text.Length, false) };
                Array.Fill(matches, true, position, word.Length);
                position += word.Length;
            }
        }
        var spans = new List<SearchMatchSpan>();
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && matches[i] == matches[start]) continue;
            spans.Add(new(start, i - start, matches[start]));
            start = i;
        }
        return spans;
    }
}
