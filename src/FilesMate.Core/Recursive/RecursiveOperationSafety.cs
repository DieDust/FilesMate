namespace FilesMate.Core.Recursive;

/// <summary>
/// Bounded compatibility checks for existing case-insensitive operation planners. These flags
/// describe observations in a captured tree, not the filesystem's case-sensitivity capability.
/// </summary>
[Flags]
public enum RecursiveOperationRisk : byte
{
    None = 0,
    CaseFoldCollision = 1,
    IncompleteIdentity = 2,
    IncompleteSubtree = 4,
    InvalidSelection = 8,
    EmptySelection = 16,
    UnverifiedCompatibility = 32,
}

public enum RecursiveOperationScope
{
    /// <summary>For path-identity consumers such as tags that do not recursively operate on descendants.</summary>
    IdentityOnly,
    /// <summary>For transfer, delete, rename, archive and undo-producing operations on a selected subtree.</summary>
    Subtree,
}

public readonly record struct RecursiveOperationSafetyResult(RecursiveOperationRisk Risk, int SelectedCount, int UnsafeCount)
{
    public bool IsSafe => SelectedCount > 0 && Risk == RecursiveOperationRisk.None;
}

/// <summary>
/// Two byte arrays per projection. One forward and one reverse pass compute inherited identity risk
/// and descendant subtree risk, independent of filter/collapse visibility. Selection is then O(k).
/// There are no retained full paths, filesystem calls, case-folded row merges, or per-selection tree walks.
/// </summary>
internal sealed class RecursiveOperationSafety
{
    private readonly RecursiveOperationRisk[] _identity;
    private readonly RecursiveOperationRisk[] _subtree;

    private RecursiveOperationSafety(RecursiveOperationRisk[] identity, RecursiveOperationRisk[] subtree)
    {
        _identity = identity;
        _subtree = subtree;
    }

    internal static RecursiveOperationSafety Build(RecursiveSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = new RecursiveOperationRisk[snapshot.Count + 1];
        var subtree = new RecursiveOperationRisk[snapshot.Count + 1];
        identity[0] = snapshot.RootListingComplete ? RecursiveOperationRisk.None : RecursiveOperationRisk.IncompleteIdentity;
        if (snapshot.RootOperationCompatibility is RecursiveOperationCompatibility.Pending or RecursiveOperationCompatibility.Unverified)
            identity[0] |= RecursiveOperationRisk.UnverifiedCompatibility;
        for (var id = 1; id <= snapshot.Count; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = snapshot.GetNode(id);
            var inherited = identity[node.ParentId];
            if (node.ParentId != 0 && (!snapshot.CompleteEnumerationScope || !snapshot.GetNode(node.ParentId).ListingComplete))
                inherited |= RecursiveOperationRisk.IncompleteIdentity;
            var direct = node.HasCaseFoldCollision ? RecursiveOperationRisk.CaseFoldCollision : RecursiveOperationRisk.None;
            if (node.OperationCompatibility is RecursiveOperationCompatibility.Pending or RecursiveOperationCompatibility.Unverified)
                direct |= RecursiveOperationRisk.UnverifiedCompatibility;
            identity[id] = inherited | direct;
            subtree[id] = direct;
            if (node.IsDirectory && (!snapshot.CompleteEnumerationScope || !node.ListingComplete))
                subtree[id] |= RecursiveOperationRisk.IncompleteSubtree;
        }
        for (var id = snapshot.Count; id > 0; id--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            subtree[snapshot.GetNode(id).ParentId] |= subtree[id];
        }
        return new(identity, subtree);
    }

    internal RecursiveOperationRisk GetRisk(int id, RecursiveOperationScope scope) => scope switch
    {
        RecursiveOperationScope.IdentityOnly => _identity[id],
        RecursiveOperationScope.Subtree => _identity[id] | _subtree[id],
        _ => RecursiveOperationRisk.InvalidSelection,
    };

    internal static RecursiveOperationSafetyResult Evaluate(RecursiveProjection projection, IEnumerable<int> selectedIds,
        RecursiveOperationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedIds);
        cancellationToken.ThrowIfCancellationRequested();
        var seen = new HashSet<int>();
        var combined = RecursiveOperationRisk.None;
        var unsafeCount = 0;
        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(id)) continue;
            var risk = projection.GetOperationRisk(id, scope);
            combined |= risk;
            if (risk != RecursiveOperationRisk.None) unsafeCount++;
        }
        if (seen.Count == 0) combined |= RecursiveOperationRisk.EmptySelection;
        return new(combined, seen.Count, unsafeCount);
    }
}
