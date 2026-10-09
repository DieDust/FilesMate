namespace FilesMate.Core.Entries;

public sealed class EntrySourcePrefixProof
{
    private readonly EntryViewIndex _candidate, _previous;
    internal EntrySourcePrefixProof(EntryViewIndex candidate, EntryViewIndex previous, bool samePrefix,
        int rangeStart = -1, int removedCount = 0, int addedCount = 0)
        => (_candidate, _previous, SamePrefix, RangeStart, RemovedCount, AddedCount)
            = (candidate, previous, samePrefix, rangeStart, removedCount, addedCount);
    public bool SamePrefix { get; }
    public int RangeStart { get; }
    public int RemovedCount { get; }
    public int AddedCount { get; }
    public bool HasSingleRangeChange => RangeStart >= 0 && (RemovedCount == 0) != (AddedCount == 0);
    public bool HasRangeReplacement => RangeStart >= 0 && RemovedCount > 0 && AddedCount > 0;
    public bool Matches(EntryViewIndex candidate, EntryViewIndex? previous)
        => ReferenceEquals(_candidate, candidate) && ReferenceEquals(_previous, previous);
}
