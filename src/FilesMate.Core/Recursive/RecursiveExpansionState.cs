namespace FilesMate.Core.Recursive;

/// <summary>UI-owned desired expansion state. Direct children start collapsed, including later scan arrivals.</summary>
public sealed class RecursiveExpansionState
{
    private readonly Dictionary<int, RecursiveExpansionIntent> _rows = [];
    private readonly Dictionary<int, RecursiveExpansionIntent> _branches = [];
    private long _sequence;

    public void SetExpanded(int entryId, bool expanded, bool subtree = false)
        => SetExpandedWithUndo(entryId, expanded, subtree);

    public RecursiveExpansionChange SetExpandedWithUndo(int entryId, bool expanded, bool subtree = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryId);
        var map = subtree ? _branches : _rows;
        RecursiveExpansionIntent? previous = map.TryGetValue(entryId, out var existing) ? existing : null;
        var intent = new RecursiveExpansionIntent(++_sequence, expanded);
        map[entryId] = intent;
        return new(entryId, subtree, previous, intent);
    }

    private static int ScopeDepth(RecursiveSnapshot snapshot, int entryId, int depth)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (depth is < 0 or > 5 && depth != int.MaxValue) throw new ArgumentOutOfRangeException(nameof(depth));
        if (entryId < 0 || entryId > snapshot.Count) throw new ArgumentOutOfRangeException(nameof(entryId));
        if (entryId != 0 && !snapshot.GetNode(entryId).IsDirectory) throw new ArgumentException("Depth scope must be a directory.", nameof(entryId));
        return entryId == 0 ? -1 : snapshot.GetNode(entryId).Depth;
    }

    /// <summary>Exact depth counts descendants; virtual-root children are level one. Future arrivals inherit this bound.</summary>
    public RecursiveExpansionChange SetDepthWithUndo(RecursiveSnapshot snapshot, int entryId, int depth)
    {
        var origin = ScopeDepth(snapshot, entryId, depth);
        var change = SetExpandedWithUndo(entryId, depth != 0, subtree: true);
        var after = change.After with { ExclusiveDirectoryDepth = depth == int.MaxValue ? null : origin + depth };
        RestoreIntent(entryId, true, after);
        return change with { After = after };
    }

    /// <summary>Add this depth without collapsing deeper explicit state or changing ordinary depth preferences.</summary>
    public RecursiveExpansionChange EnsureDepthWithUndo(RecursiveSnapshot snapshot, int entryId, int depth)
    {
        var origin = ScopeDepth(snapshot, entryId, depth);
        if (depth is < 1 or > 5 && depth != int.MaxValue) throw new ArgumentOutOfRangeException(nameof(depth));
        RecursiveExpansionIntent? before = _branches.TryGetValue(entryId, out var current) ? current : null;
        var limit = depth == int.MaxValue ? int.MaxValue : origin + depth;
        // Consecutive identical input has no new intention and no extra undo record.
        if (current.DepthFloors?.Any(f => f.ExclusiveDirectoryDepth == limit && f.Sequence == _sequence) == true)
            return new(entryId, true, before, current);
        var floors = (current.DepthFloors ?? []).Where(f => f.ExclusiveDirectoryDepth != limit)
            .Append(new RecursiveExpansionFloor(++_sequence, limit)).ToArray();
        // Five finite depths plus all levels keep the retained per-scope floor list bounded.
        var after = current with { DepthFloors = floors };
        _branches[entryId] = after;
        return new(entryId, true, before, after);
    }

    public void RestoreIntent(int entryId, bool subtree, RecursiveExpansionIntent? intent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryId);
        var map = subtree ? _branches : _rows;
        if (intent is { } value)
        {
            map[entryId] = value;
            _sequence = Math.Max(_sequence, value.Sequence);
            foreach (var floor in value.DepthFloors ?? []) _sequence = Math.Max(_sequence, floor.Sequence);
        }
        else map.Remove(entryId);
    }

    public bool IsExpanded(RecursiveSnapshot snapshot, int entryId) => RecursiveExpansionPlan.Resolve(snapshot, entryId, _rows, _branches);
    public bool IsSubtreeExpanded(RecursiveSnapshot snapshot, int entryId) => RecursiveExpansionPlan.ResolveSubtree(snapshot, entryId, _rows, _branches);
    public RecursiveExpansionPlan Capture() => new(new(_rows), new(_branches));
    public void Clear() { _rows.Clear(); _branches.Clear(); _sequence = 0; }
}

internal readonly record struct RecursiveExpansionFloor(long Sequence, int ExclusiveDirectoryDepth);
public readonly record struct RecursiveExpansionIntent(long Sequence, bool Expanded)
{
    public int? ExclusiveDirectoryDepth { get; init; }
    internal RecursiveExpansionFloor[]? DepthFloors { get; init; }
    internal bool Includes(RecursiveNode node) => Expanded && (ExclusiveDirectoryDepth is null || node.Depth < ExclusiveDirectoryDepth);
    internal long FloorSequence(int depth)
    {
        var sequence = 0L;
        foreach (var floor in DepthFloors ?? [])
            if (depth < floor.ExclusiveDirectoryDepth) sequence = Math.Max(sequence, floor.Sequence);
        return sequence;
    }
}
public readonly record struct RecursiveExpansionChange(int EntryId, bool Subtree, RecursiveExpansionIntent? Before, RecursiveExpansionIntent After);

/// <summary>Immutable expansion intentions; subtree intentions also apply to directories discovered later.</summary>
public sealed class RecursiveExpansionPlan
{
    private readonly Dictionary<int, RecursiveExpansionIntent> _rows;
    private readonly Dictionary<int, RecursiveExpansionIntent> _branches;
    internal RecursiveExpansionPlan(Dictionary<int, RecursiveExpansionIntent> rows, Dictionary<int, RecursiveExpansionIntent> branches) => (_rows, _branches) = (rows, branches);
    public bool IsExpanded(RecursiveSnapshot snapshot, int entryId) => Resolve(snapshot, entryId, _rows, _branches);
    public bool IsSubtreeExpanded(RecursiveSnapshot snapshot, int entryId) => ResolveSubtree(snapshot, entryId, _rows, _branches);

    internal RecursiveExpansionIntent BranchIntent(int entryId, RecursiveExpansionIntent inherited, int directoryDepth = -1)
    {
        _branches.TryGetValue(entryId, out var local);
        var basis = local.Sequence > inherited.Sequence ? local : inherited;
        var inheritedFloors = inherited.DepthFloors;
        var localFloors = local.DepthFloors;
        // The ordinary path allocates nothing. Reuse an unchanged active floor list
        // for wide sibling lists; only boundaries or another user scope need a merge.
        if (inheritedFloors is null && localFloors is null) return basis;
        return MergeActiveFloors(basis, inheritedFloors, localFloors, directoryDepth);
    }

    // Keep all captured lambdas out of BranchIntent: the compiler allocates their
    // display class before its no-floor return, once for every visited entry.
    private static RecursiveExpansionIntent MergeActiveFloors(RecursiveExpansionIntent basis,
        RecursiveExpansionFloor[]? inherited, RecursiveExpansionFloor[]? local, int directoryDepth)
    {
        if (local is null)
        {
            var unchanged = true;
            foreach (var floor in inherited!)
                if (floor.Sequence <= basis.Sequence || floor.ExclusiveDirectoryDepth <= directoryDepth)
                { unchanged = false; break; }
            if (unchanged) return basis with { DepthFloors = inherited };
        }
        var active = new List<RecursiveExpansionFloor>();
        foreach (var source in new[] { inherited, local })
        {
            if (source is null) continue;
            foreach (var floor in source)
            {
                if (floor.Sequence <= basis.Sequence || floor.ExclusiveDirectoryDepth <= directoryDepth) continue;
                var found = -1;
                for (var i = 0; i < active.Count; i++)
                    if (active[i].ExclusiveDirectoryDepth == floor.ExclusiveDirectoryDepth) { found = i; break; }
                if (found < 0) active.Add(floor);
                else if (floor.Sequence > active[found].Sequence) active[found] = floor;
            }
        }
        return basis with { DepthFloors = active.Count == 0 ? null : active.ToArray() };
    }

    internal bool IsExpanded(RecursiveNode node, RecursiveExpansionIntent branch)
    {
        _rows.TryGetValue(node.Id, out var row);
        var newest = row.Sequence > branch.Sequence ? row : branch;
        return node.IsDirectory && !node.TraversalSkipped && (branch.FloorSequence(node.Depth) > newest.Sequence || newest.Includes(node));
    }

    internal static bool ResolveSubtree(RecursiveSnapshot snapshot, int entryId,
        IReadOnlyDictionary<int, RecursiveExpansionIntent> rows, IReadOnlyDictionary<int, RecursiveExpansionIntent> branches)
    {
        if (entryId != 0 && (!snapshot.GetNode(entryId).IsDirectory || snapshot.GetNode(entryId).TraversalSkipped)) return false;
        RecursiveExpansionIntent BranchAt(int id)
        {
            var intent = default(RecursiveExpansionIntent);
            while (true)
            {
                if (branches.TryGetValue(id, out var branch) && branch.Sequence > intent.Sequence) intent = branch;
                if (id == 0) return intent;
                id = snapshot.GetNode(id).ParentId;
            }
        }
        bool InScope(int id)
        {
            if (entryId == 0) return true;
            while (id != 0 && id != entryId) id = snapshot.GetNode(id).ParentId;
            return id == entryId;
        }
        if (!BranchAt(entryId).Expanded || BranchAt(entryId).ExclusiveDirectoryDepth is not null || !Resolve(snapshot, entryId, rows, branches)) return false;
        foreach (var (id, intent) in rows)
            if (!intent.Expanded && InScope(id) && !Resolve(snapshot, id, rows, branches)) return false;
        foreach (var (id, intent) in branches)
            if ((!intent.Expanded || intent.ExclusiveDirectoryDepth is not null) && InScope(id)
                && (!BranchAt(id).Expanded || BranchAt(id).ExclusiveDirectoryDepth is not null)) return false;
        return true;
    }

    internal static bool Resolve(RecursiveSnapshot snapshot, int entryId,
        IReadOnlyDictionary<int, RecursiveExpansionIntent> rows, IReadOnlyDictionary<int, RecursiveExpansionIntent> branches)
    {
        var depth = entryId == 0 ? -1 : snapshot.GetNode(entryId).Depth;
        if (entryId != 0 && (!snapshot.GetNode(entryId).IsDirectory || snapshot.GetNode(entryId).TraversalSkipped)) return false;
        rows.TryGetValue(entryId, out var newest);
        var current = entryId;
        var floorSequence = 0L;
        while (true)
        {
            if (branches.TryGetValue(current, out var branch))
            {
                if (branch.Sequence > newest.Sequence) newest = branch;
                floorSequence = Math.Max(floorSequence, branch.FloorSequence(depth));
            }
            if (current == 0) break;
            current = snapshot.GetNode(current).ParentId;
        }
        return floorSequence > newest.Sequence || (entryId == 0 ? newest.Expanded : newest.Includes(snapshot.GetNode(entryId)));
    }
}
