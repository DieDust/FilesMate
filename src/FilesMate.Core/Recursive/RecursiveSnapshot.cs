using FilesMate.Core.Directories;
using FilesMate.Core.Entries;

namespace FilesMate.Core.Recursive;

/// <summary>
/// Immutable, internally coherent tree publication. Entry and node blocks share unchanged storage
/// with prior publications. Ids never change within SessionId; a refresh creates a new session.
/// </summary>
public sealed class RecursiveSnapshot
{
    internal const int BlockShift = 8;
    internal const int BlockSize = 1 << BlockShift;
    private readonly RecursiveNode[][] _nodes;
    internal EntryStore Store { get; }

    internal RecursiveSnapshot(Guid sessionId, long generation, long revision, string rootPath,
        EntryStore store, EntrySnapshot entries, RecursiveNode[][] nodes, RecursiveSessionState state,
        int skippedDirectories, int skippedEntries, RecursiveLimit limits, DirectoryReadError? error,
        RecursiveTraversalState rootTraversalState, bool rootListingOmittedEntries, bool completeEnumerationScope,
        RecursiveOperationCompatibility rootOperationCompatibility)
    {
        SessionId = sessionId;
        Generation = generation;
        Revision = revision;
        RootPath = rootPath;
        Store = store;
        Entries = entries;
        _nodes = nodes;
        State = state;
        SkippedDirectories = skippedDirectories;
        SkippedEntries = skippedEntries;
        Limits = limits;
        Error = error;
        RootTraversalState = rootTraversalState;
        RootListingOmittedEntries = rootListingOmittedEntries;
        CompleteEnumerationScope = completeEnumerationScope;
        RootOperationCompatibility = rootOperationCompatibility;
    }

    public Guid SessionId { get; }
    public long Generation { get; }
    public long Revision { get; }
    public string RootPath { get; }
    public EntrySnapshot Entries { get; }
    public int Count => Entries.Count;
    public RecursiveSessionState State { get; }
    public RecursiveScanBudget Budget { get; init; }
    public int SkippedDirectories { get; }
    public int SkippedEntries { get; }
    public RecursiveLimit Limits { get; }
    public bool LimitReached => Limits != RecursiveLimit.None;
    public DirectoryReadError? Error { get; }
    public RecursiveTraversalState RootTraversalState { get; }
    public RecursiveOperationCompatibility RootOperationCompatibility { get; }
    public bool RootListingOmittedEntries { get; }
    /// <summary>False if hidden or system entries were excluded by the directory provider request.</summary>
    public bool CompleteEnumerationScope { get; }
    public bool RootListingComplete => CompleteEnumerationScope && !RootListingOmittedEntries
        && RootTraversalState == RecursiveTraversalState.Completed;

    public RecursiveNode GetNode(int entryId)
    {
        if (entryId < 1 || entryId > Count) throw new ArgumentOutOfRangeException(nameof(entryId));
        var index = entryId - 1;
        return _nodes[index >> BlockShift][index & (BlockSize - 1)];
    }

    public FileEntryCore GetEntry(int entryId)
    {
        _ = GetNode(entryId);
        return Entries[entryId - 1];
    }

    public string ResolvePath(int entryId) => entryId == 0 ? RootPath
        : RecursivePath.Combine(RootPath, ResolveRelativePath(entryId));

    public string ResolveRelativePath(int entryId)
    {
        var node = GetNode(entryId);
        var names = new string[node.Depth + 1];
        for (var i = names.Length - 1; i >= 0; i--)
        {
            names[i] = Entries[node.Id - 1].Name;
            if (node.ParentId != 0) node = GetNode(node.ParentId);
        }
        return string.Join(RecursivePath.Separator(RootPath), names);
    }

    public string ResolveRelativeParentPath(int entryId)
    {
        var parent = GetNode(entryId).ParentId;
        return parent == 0 ? string.Empty : ResolveRelativePath(parent);
    }

    /// <summary>Recursive branches show direct files before child folders, retaining the selected sibling key and direction.</summary>
    public static EntrySort NormalizeTreeSort(EntrySort sort) => sort with
    {
        Grouping = EntryGrouping.FilesFirst,
        DirectoriesFirst = false,
    };

    /// <summary>
    /// CPU-only work; call off the UI thread. Filtering includes required ancestors as context rows,
    /// excludes them from MatchCount, and temporarily reveals matching descendants beneath collapsed nodes.
    /// All sorting is confined to siblings. No filesystem, metadata, or folder-size reads occur here.
    /// </summary>
    public RecursiveProjection BuildProjection(EntrySort sort, EntryFilter filter, IComparer<string> nameComparer,
        IReadOnlySet<int>? collapsedNodeIds = null, CancellationToken cancellationToken = default,
        Func<FileEntryCore, bool>? additionalMatch = null,
        IReadOnlyDictionary<int, EntryPropertyValue>? propertyValues = null,
        FileAttributes excludedAttributes = 0, RecursiveExpansionPlan? expansionPlan = null)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(nameComparer);
        cancellationToken.ThrowIfCancellationRequested();
        sort = NormalizeTreeSort(sort);
        // Reuse the existing key semantics (including natural/pinyin ordering) once for all entries.
        // A globally sorted sequence's subsequence for each parent has the same sibling ordering.
        // Every sibling has the same parent location: use names as the location tie-breaker.
        // FullPath comparison within siblings is equivalent to leaf-name comparison because the
        // full parent prefix is identical. Neither column is a global flattening of the tree.
        var siblingSort = sort.Column == EntrySortColumn.Location ? sort with { Column = EntrySortColumn.Name } : sort;
        var ordered = EntryViewIndex.Build(Entries, siblingSort, EntryFilter.None, nameComparer, Generation,
            cancellationToken: cancellationToken, propertyValues: propertyValues);
        var firstChild = new int[Count + 1];
        var lastChild = new int[Count + 1];
        var nextSibling = new int[Count + 1];
        var matches = new bool[Count + 1];
        var included = new bool[Count + 1];
        var hasIncludedChildren = new bool[Count + 1];
        var suppressed = new bool[Count + 1];
        var effectiveExcluded = excludedAttributes | (filter.IncludeHidden ? 0 : FileAttributes.Hidden);
        for (var id = 1; id <= Count; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            suppressed[id] = suppressed[GetNode(id).ParentId] || (Entries[id - 1].Attributes & effectiveExcluded) != 0;
        }
        var matchCount = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = ordered[i] + 1;
            var parent = GetNode(id).ParentId;
            if (lastChild[parent] == 0) firstChild[parent] = id;
            else nextSibling[lastChild[parent]] = id;
            lastChild[parent] = id;
            if (!suppressed[id] && filter.Matches(Entries[id - 1]) && (additionalMatch is null || additionalMatch(Entries[id - 1])))
            {
                matches[id] = included[id] = true;
                matchCount++;
            }
        }
        // Parents always precede descendants in source identity order, regardless of traversal order.
        for (var id = Count; id > 0; id--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (included[id])
            {
                var parent = GetNode(id).ParentId;
                included[parent] = true;
                hasIncludedChildren[parent] = true;
            }
        }
        // Attribute preferences define visibility, not a request to reveal collapsed branches.
        var filtering = !string.IsNullOrEmpty(filter.Query) || filter.Metadata.IsActive || filter.FilesOnly || filter.DirectoriesOnly || additionalMatch is not null;
        var safety = RecursiveOperationSafety.Build(this, cancellationToken);
        var basis = new RecursiveProjectionBasis(this, sort, filter, firstChild, nextSibling,
            matches, included, hasIncludedChildren, filtering, matchCount, safety);
        return basis.Build(collapsedNodeIds, expansionPlan, cancellationToken);
    }

    internal RecursiveSnapshot WithState(RecursiveSessionState state, long revision) => new(SessionId,
        Generation, revision, RootPath, Store, Entries, _nodes, state, SkippedDirectories, SkippedEntries, Limits, Error,
        RootTraversalState, RootListingOmittedEntries, CompleteEnumerationScope, RootOperationCompatibility) { Budget = Budget };

    internal static RecursiveNode[][] CaptureNodes(List<RecursiveNode> nodes, RecursiveSnapshot? previous,
        HashSet<int> dirtyBlocks, CancellationToken token)
    {
        var blocks = new RecursiveNode[(nodes.Count + BlockSize - 1) >> BlockShift][];
        for (var i = 0; i < blocks.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var offset = i << BlockShift;
            var length = Math.Min(BlockSize, nodes.Count - offset);
            if (previous is not null && i < previous._nodes.Length && previous._nodes[i].Length == length
                && !dirtyBlocks.Contains(i)) blocks[i] = previous._nodes[i];
            else
            {
                blocks[i] = new RecursiveNode[length];
                nodes.CopyTo(offset, blocks[i], 0, length);
            }
        }
        return blocks;
    }
}

/// <summary>
/// Captured adapter for the existing virtualized entry surface. Store is an append-only compatibility
/// object owned by RecursiveSession: callers must never reconcile, rename, upsert, or remove from it.
/// Always read rows and paths through this publication rather than the live Store.
/// </summary>
public sealed class RecursiveProjection
{
    private readonly bool[] _matches;
    private readonly bool[] _hasIncludedChildren;
    private readonly HashSet<int> _collapsed;
    private readonly Dictionary<int, int> _visible = [];
    private readonly RecursiveProjectionBasis _basis;
    private readonly RecursiveExpansionPlan? _expansionPlan;
    private readonly RecursiveOperationSafety _safety;
    internal RecursiveProjection(RecursiveSnapshot snapshot, EntryViewIndex index, bool[] matches,
        bool[] hasIncludedChildren, HashSet<int> collapsed, bool filtering, int matchCount, RecursiveOperationSafety safety, RecursiveProjectionBasis basis, RecursiveExpansionPlan? expansionPlan)
    {
        Snapshot = snapshot;
        Index = index;
        _matches = matches;
        _hasIncludedChildren = hasIncludedChildren;
        _collapsed = collapsed;
        _safety = safety;
        _basis = basis;
        _expansionPlan = expansionPlan;
        for (var view = 0; view < index.Count; view++)
        {
            var id = index[view] + 1;
            _visible.Add(id, view);
            MaximumVisibleDepth = Math.Max(MaximumVisibleDepth, snapshot.GetNode(id).Depth);
        }
        IsFiltering = filtering;
        MatchCount = matchCount;
    }

    public RecursiveSnapshot Snapshot { get; }
    public EntryStore Store => Snapshot.Store;
    public EntryViewIndex Index { get; }
    public Guid SessionId => Snapshot.SessionId;
    public long Revision => Snapshot.Revision;
    public long Generation => Snapshot.Generation;
    public RecursiveSessionState State => Snapshot.State;
    public int MatchCount { get; }
    public int MaximumVisibleDepth { get; }
    public bool IsFiltering { get; }
    public RecursiveNode GetNode(int entryId) => Snapshot.GetNode(entryId);
    public string ResolvePath(int entryId) => Snapshot.ResolvePath(entryId);
    public string ResolveRelativePath(int entryId) => Snapshot.ResolveRelativePath(entryId);
    public string ResolveRelativeParentPath(int entryId) => Snapshot.ResolveRelativeParentPath(entryId);
    public bool IsContextOnly(int entryId) { _ = GetNode(entryId); return !_matches[entryId]; }
    public bool IsVisible(int entryId) => _visible.ContainsKey(entryId);
    public int ViewIndexOfId(int entryId) => _visible.GetValueOrDefault(entryId, -1);
    public bool SharesStructureWith(RecursiveProjection other) => ReferenceEquals(_basis, other._basis);
    public bool RequiresSnapshotCatchUp(RecursiveSnapshot latest) => latest.SessionId == SessionId
        && latest.Generation == Generation && latest.Revision > Revision;

    /// <summary>Same exact snapshot, sibling ordering, filter context and safety; rebuild only visible expansion.</summary>
    public RecursiveProjection RebuildExpansion(RecursiveExpansionPlan expansionPlan, CancellationToken cancellationToken = default)
        => _basis.Build(null, expansionPlan, cancellationToken);
    public bool IsSelectable(int entryId) => IsVisible(entryId) && _matches[entryId];
    /// <summary>Whether this row has a child included by current visibility/filter rules, even while collapsed.</summary>
    public bool HasChildren(int entryId) { _ = GetNode(entryId); return _hasIncludedChildren[entryId]; }
    // Included-child bits already include files, folders and current filters.
    // No filesystem lookup is performed by this presentation query.
    public RecursiveChildState GetChildState(int entryId, bool observationsCurrent = true)
    {
        var node = GetNode(entryId);
        if (!node.IsDirectory) return RecursiveChildState.NotDirectory;
        if (node.TraversalState == RecursiveTraversalState.Failed) return RecursiveChildState.Error;
        if (node.TraversalSkipped) return RecursiveChildState.Unavailable;
        if (HasChildren(entryId)) return RecursiveChildState.HasChildren;
        if (_basis.HasPhysicalChildren(entryId)) return RecursiveChildState.Excluded;
        if (!observationsCurrent) return RecursiveChildState.Unknown;
        if (node.ListingComplete && Snapshot.CompleteEnumerationScope) return RecursiveChildState.Empty;
        return node.TraversalState == RecursiveTraversalState.Enumerating
            ? RecursiveChildState.Loading : RecursiveChildState.Unknown;
    }
    public bool IsExpanded(int entryId) => GetNode(entryId).IsDirectory
        && HasChildren(entryId) && (IsFiltering
        || (_expansionPlan is not null ? _expansionPlan.IsExpanded(Snapshot, entryId) : !_collapsed.Contains(entryId)));

    /// <summary>Observed compatibility risk for legacy case-insensitive planners; never hides or merges identities.</summary>
    public RecursiveOperationRisk GetOperationRisk(int entryId, RecursiveOperationScope scope = RecursiveOperationScope.Subtree) =>
        !IsSelectable(entryId) ? RecursiveOperationRisk.InvalidSelection : _safety.GetRisk(entryId, scope);

    public bool IsOperationSafe(int entryId, RecursiveOperationScope scope = RecursiveOperationScope.Subtree) =>
        GetOperationRisk(entryId, scope) == RecursiveOperationRisk.None;

    /// <summary>Rejects an unsafe selection explicitly before any existing path-plan deduplication is used.</summary>
    public RecursiveOperationSafetyResult EvaluateOperationSafety(IEnumerable<int> selectedIds,
        RecursiveOperationScope scope = RecursiveOperationScope.Subtree, CancellationToken cancellationToken = default) =>
        RecursiveOperationSafety.Evaluate(this, selectedIds, scope, cancellationToken);

    /// <summary>Captures only selected, visible direct matches and removes descendants covered by selected directories.</summary>
    public IReadOnlyList<string> ResolveOperationPaths(IEnumerable<int> selectedIds,
        CancellationToken cancellationToken = default) => RecursiveOperationSelection.ResolvePaths(this, selectedIds, cancellationToken);
}


/// <summary>Immutable structural work, owned by one exact snapshot publication. Never shared across revisions.</summary>
internal sealed class RecursiveProjectionBasis
{
    internal bool HasPhysicalChildren(int entryId) => _firstChild[entryId] != 0;
    private readonly RecursiveSnapshot _snapshot;
    private readonly EntrySort _sort;
    private readonly EntryFilter _filter;
    private readonly int[] _firstChild, _nextSibling;
    private readonly bool[] _matches, _included, _hasIncludedChildren;
    private readonly bool _filtering;
    private readonly int _matchCount;
    private readonly RecursiveOperationSafety _safety;

    internal RecursiveProjectionBasis(RecursiveSnapshot snapshot, EntrySort sort, EntryFilter filter,
        int[] firstChild, int[] nextSibling, bool[] matches, bool[] included, bool[] hasIncludedChildren,
        bool filtering, int matchCount, RecursiveOperationSafety safety)
    {
        _snapshot = snapshot; _sort = sort; _filter = filter; _firstChild = firstChild; _nextSibling = nextSibling;
        _matches = matches; _included = included; _hasIncludedChildren = hasIncludedChildren;
        _filtering = filtering; _matchCount = matchCount; _safety = safety;
    }

    internal RecursiveProjection Build(IReadOnlySet<int>? collapsedNodeIds, RecursiveExpansionPlan? expansionPlan,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var collapsed = collapsedNodeIds is null ? new HashSet<int>() : new HashSet<int>(collapsedNodeIds);
        var visible = new List<int>(Math.Min(_snapshot.Count, 2048));
        var pending = new Stack<(int Sibling, RecursiveExpansionIntent Branch)>();
        var current = _firstChild[0];
        var inherited = expansionPlan?.BranchIntent(0, default) ?? default;
        while (current != 0)
        {
            token.ThrowIfCancellationRequested();
            var next = _nextSibling[current];
            if (_included[current])
            {
                visible.Add(current - 1);
                var node = _snapshot.GetNode(current);
                // Files cannot own descendants; only directories need branch/floor resolution.
                var branch = node.IsDirectory ? expansionPlan?.BranchIntent(current, inherited, node.Depth) ?? default : inherited;
                var expanded = node.IsDirectory && (expansionPlan is not null ? expansionPlan.IsExpanded(node, branch) : !collapsed.Contains(current));
                if (_firstChild[current] != 0 && (_filtering || expanded))
                {
                    if (next != 0) pending.Push((next, inherited));
                    inherited = branch;
                    current = _firstChild[current];
                    continue;
                }
            }
            if (next != 0) current = next;
            else if (pending.TryPop(out var ancestorSibling)) (current, inherited) = ancestorSibling;
            else current = 0;
        }
        var index = EntryViewIndex.FromSourceIndexes(_snapshot.Entries, visible, _snapshot.Generation, _sort, _filter, token,
            allowAppendProgress: _snapshot.State is RecursiveSessionState.Loading or RecursiveSessionState.Cancelled);
        return new RecursiveProjection(_snapshot, index, _matches, _hasIncludedChildren, collapsed, _filtering,
            _matchCount, _safety, this, expansionPlan);
    }
}
