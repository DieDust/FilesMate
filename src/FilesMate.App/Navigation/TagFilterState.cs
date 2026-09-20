using FilesMate.Core.Entries;

namespace FilesMate.App.Navigation;

/// <summary>
/// Immutable, in-memory tag filter for one published directory generation.
/// It stores entry ids, so filtering only rebuilds the index and never re-enumerates.
/// </summary>
public sealed record TagFilterState(string Label, IReadOnlySet<int> MatchingEntryIds)
{
    public bool Matches(in FileEntryCore entry) => MatchingEntryIds.Contains(entry.Id);
}
