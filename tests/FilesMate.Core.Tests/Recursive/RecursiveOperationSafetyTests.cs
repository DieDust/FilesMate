using System.Runtime.CompilerServices;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveOperationSafetyTests
{
    [Fact]
    public async Task Case_fold_file_aliases_keep_both_rows_ids_paths_and_explicit_unsafe_results()
    {
        var snapshot = await ScanAsync(Tree((Root, [File("A"), File("a"), File("safe.txt")])));
        var projection = Project(snapshot);
        var upper = Id(snapshot, "A");
        var lower = Id(snapshot, "a");
        Assert.Equal(3, snapshot.Count);
        Assert.NotEqual(upper, lower);
        Assert.Equal(0, snapshot.SkippedEntries);
        Assert.Equal(new[] { Root + @"\A", Root + @"\a" }, projection.ResolveOperationPaths([upper, lower]));
        foreach (var id in new[] { upper, lower })
        {
            Assert.True(snapshot.GetNode(id).HasCaseFoldCollision);
            Assert.True(projection.IsSelectable(id));
            Assert.Equal(RecursiveOperationRisk.CaseFoldCollision, projection.GetOperationRisk(id));
            Assert.Equal(RecursiveOperationRisk.CaseFoldCollision,
                projection.GetOperationRisk(id, RecursiveOperationScope.IdentityOnly));
            Assert.False(projection.IsOperationSafe(id));
        }
        AssertSafety(projection, [upper, lower], RecursiveOperationRisk.CaseFoldCollision, 2, 2);
        AssertSafety(projection, [Id(snapshot, "safe.txt")], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Children_beneath_case_fold_directory_aliases_inherit_identity_risk_without_name_deduplication()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("A"), Directory("a")]),
            (Root + @"\A", [File("x")]), (Root + @"\a", [File("x")])));
        var projection = Project(snapshot);
        var upper = Id(snapshot, @"A\x");
        var lower = Id(snapshot, @"a\x");
        Assert.Equal(4, snapshot.Count);
        Assert.NotEqual(upper, lower);
        Assert.NotEqual(snapshot.GetNode(upper).ParentId, snapshot.GetNode(lower).ParentId);
        Assert.Equal(new[] { Root + @"\A\x", Root + @"\a\x" }, projection.ResolveOperationPaths([upper, lower]));
        foreach (var id in new[] { upper, lower })
        {
            Assert.False(snapshot.GetNode(id).HasCaseFoldCollision);
            Assert.Equal(RecursiveOperationRisk.CaseFoldCollision,
                projection.GetOperationRisk(id, RecursiveOperationScope.IdentityOnly));
        }
        AssertSafety(projection, [upper, lower], RecursiveOperationRisk.CaseFoldCollision, 2, 2);
    }

    [Fact]
    public async Task Cross_level_alias_selection_is_guarded_before_any_path_based_ancestor_elision()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("A"), Directory("a")]),
            (Root + @"\A", []), (Root + @"\a", [File("x")])));
        var projection = Project(snapshot);
        var directory = Id(snapshot, "A");
        var otherChild = Id(snapshot, @"a\x");
        Assert.Equal(new[] { Root + @"\A", Root + @"\a\x" },
            projection.ResolveOperationPaths([directory, otherChild]));
        AssertSafety(projection, [directory, otherChild], RecursiveOperationRisk.CaseFoldCollision, 2, 2);
        AssertSafety(projection, [otherChild, directory], RecursiveOperationRisk.CaseFoldCollision, 2, 2);
    }

    [Fact]
    public async Task Mixed_file_and_directory_aliases_are_both_retained_and_guarded()
    {
        var snapshot = await ScanAsync(Tree((Root, [File("A"), Directory("a")]), (Root + @"\a", [File("x")])));
        var projection = Project(snapshot);
        Assert.Equal(3, snapshot.Count);
        Assert.False(snapshot.GetNode(Id(snapshot, "A")).IsDirectory);
        Assert.True(snapshot.GetNode(Id(snapshot, "a")).IsDirectory);
        AssertSafety(projection, [Id(snapshot, "A"), Id(snapshot, "a"), Id(snapshot, @"a\x")],
            RecursiveOperationRisk.CaseFoldCollision, 3, 3);
    }

    [Fact]
    public async Task Descendant_collision_blocks_parent_subtree_but_not_its_identity_or_unrelated_sibling()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent"), File("root.txt")]),
            (Root + @"\parent", [Directory("nested"), File("safe.txt")]),
            (Root + @"\parent\nested", [File("A"), File("a")])));
        var projection = Project(snapshot);
        foreach (var path in new[] { "parent", @"parent\nested" })
        {
            var id = Id(snapshot, path);
            Assert.False(snapshot.GetNode(id).HasCaseFoldCollision);
            Assert.True(snapshot.GetNode(id).ListingComplete);
            Assert.True(projection.IsOperationSafe(id, RecursiveOperationScope.IdentityOnly));
            Assert.Equal(RecursiveOperationRisk.None,
                projection.GetOperationRisk(id, RecursiveOperationScope.IdentityOnly));
            Assert.Equal(RecursiveOperationRisk.CaseFoldCollision, projection.GetOperationRisk(id));
        }
        AssertSafety(projection, [Id(snapshot, @"parent\safe.txt"), Id(snapshot, "root.txt")],
            RecursiveOperationRisk.None, 2, 0);
    }

    [Theory]
    [InlineData(FileAttributes.ReparsePoint, RecursiveTraversalState.SkippedReparsePoint)]
    [InlineData(FileAttributes.Offline, RecursiveTraversalState.SkippedOffline)]
    public async Task Excluded_descendant_directory_blocks_subtree_but_not_complete_parent_identity(
        FileAttributes excludedAttribute, RecursiveTraversalState traversal)
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent")]),
            (Root + @"\parent", [Directory("excluded", FileAttributes.Directory | excludedAttribute), File("safe.txt")])));
        var projection = Project(snapshot);
        var parent = Id(snapshot, "parent");
        var excluded = Id(snapshot, @"parent\excluded");
        Assert.True(snapshot.RootListingComplete);
        Assert.True(snapshot.GetNode(parent).ListingComplete);
        Assert.False(snapshot.GetNode(excluded).ListingComplete);
        Assert.Equal(traversal, snapshot.GetNode(excluded).TraversalState);
        foreach (var id in new[] { parent, excluded })
        {
            Assert.True(projection.IsOperationSafe(id, RecursiveOperationScope.IdentityOnly));
            Assert.Equal(RecursiveOperationRisk.IncompleteSubtree, projection.GetOperationRisk(id));
        }
        AssertSafety(projection, [parent], RecursiveOperationRisk.None, 1, 0, RecursiveOperationScope.IdentityOnly);
        AssertSafety(projection, [Id(snapshot, @"parent\safe.txt")], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Failed_descendant_directory_blocks_parent_subtree_without_poisoning_unrelated_file_identity()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => request.Path == Root
                ? Batches(Batch(request, [Directory("parent"), File("root.txt")]))
                : request.Path == Root + @"\parent"
                    ? Batches(Batch(request, [Directory("denied"), File("safe.txt")]))
                    : Failure(new UnauthorizedAccessException("fixture denied"))
        };
        var snapshot = await ScanAsync(enumerator);
        var projection = Project(snapshot);
        var parent = Id(snapshot, "parent");
        var denied = Id(snapshot, @"parent\denied");
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(RecursiveTraversalState.Failed, snapshot.GetNode(denied).TraversalState);
        Assert.False(snapshot.GetNode(denied).ListingComplete);
        Assert.True(snapshot.GetNode(parent).ListingComplete);
        AssertSafety(projection, [parent, denied], RecursiveOperationRisk.IncompleteSubtree, 2, 2);
        AssertSafety(projection, [parent, denied], RecursiveOperationRisk.None, 2, 0, RecursiveOperationScope.IdentityOnly);
        AssertSafety(projection, [Id(snapshot, @"parent\safe.txt"), Id(snapshot, "root.txt")],
            RecursiveOperationRisk.None, 2, 0);
    }

    [Theory]
    [InlineData(true, RecursiveTraversalState.SkippedDepthLimit)]
    [InlineData(false, RecursiveTraversalState.SkippedNodeLimit)]
    public async Task Limited_descendant_directory_blocks_subtree_and_preserves_complete_ancestor_identity(
        bool depthLimit, RecursiveTraversalState traversal)
    {
        var options = depthLimit ? new RecursiveReadOptions { MaximumDepth = 2 }
            : new RecursiveReadOptions { MaximumNodes = 3 };
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent")]),
            (Root + @"\parent", [Directory("limited"), File("safe.txt")]),
            (Root + @"\parent\limited", [File("omitted.txt")])), options);
        var projection = Project(snapshot);
        var parent = Id(snapshot, "parent");
        var limited = Id(snapshot, @"parent\limited");
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.True(snapshot.RootListingComplete);
        Assert.True(snapshot.GetNode(parent).ListingComplete);
        Assert.False(snapshot.GetNode(limited).ListingComplete);
        Assert.Equal(traversal, snapshot.GetNode(limited).TraversalState);
        AssertSafety(projection, [parent, limited], RecursiveOperationRisk.IncompleteSubtree, 2, 2);
        AssertSafety(projection, [parent, limited], RecursiveOperationRisk.None, 2, 0, RecursiveOperationScope.IdentityOnly);
        AssertSafety(projection, [Id(snapshot, @"parent\safe.txt")], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Missing_final_listing_marker_blocks_child_identity_and_ancestor_subtree()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => request.Path == Root
                ? Batches(Batch(request, [Directory("incomplete"), Directory("safe")]))
                : Batches(Batch(request, [File("child.txt")], final: request.Path.EndsWith(@"\safe")))
        };
        var snapshot = await ScanAsync(enumerator);
        var projection = Project(snapshot);
        var incomplete = Id(snapshot, "incomplete");
        Assert.True(snapshot.RootListingComplete);
        Assert.False(snapshot.GetNode(incomplete).ListingComplete);
        Assert.Equal(RecursiveOperationRisk.None,
            projection.GetOperationRisk(incomplete, RecursiveOperationScope.IdentityOnly));
        Assert.Equal(RecursiveOperationRisk.IncompleteSubtree, projection.GetOperationRisk(incomplete));
        AssertSafety(projection, [Id(snapshot, @"incomplete\child.txt")], RecursiveOperationRisk.IncompleteIdentity, 1, 1);
        AssertSafety(projection, [Id(snapshot, "safe"), Id(snapshot, @"safe\child.txt")],
            RecursiveOperationRisk.None, 2, 0);
    }

    [Fact]
    public async Task Root_and_parent_progress_snapshots_are_unsafe_until_the_required_listings_finish()
    {
        var rootWaiting = Signal();
        var rootRelease = Signal();
        var parentWaiting = Signal();
        var parentRelease = Signal();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (request.Path == Root)
            {
                yield return Batch(request, [Directory("parent"), File("root.txt")], final: false);
                rootWaiting.TrySetResult();
                await rootRelease.Task.WaitAsync(token);
                yield return Batch(request, []);
            }
            else
            {
                await Task.Delay(40, token);
                yield return Batch(request, [File("child.txt")], final: false);
                parentWaiting.TrySetResult();
                await parentRelease.Task.WaitAsync(token);
                yield return Batch(request, []);
            }
        }
        await using var session = Session(new ScriptedEnumerator { Read = Read },
            new() { ProgressInterval = TimeSpan.FromMilliseconds(16) });
        Assert.Equal(RecursiveTraversalState.Pending, session.CurrentSnapshot.RootTraversalState);
        Assert.False(session.CurrentSnapshot.RootListingComplete);
        var run = session.RunAsync();
        try
        {
            await rootWaiting.Task.WaitAsync(TestTimeout);
            var rootPending = session.CurrentSnapshot;
            var parent = Id(rootPending, "parent");
            var rootFile = Id(rootPending, "root.txt");
            Assert.Equal(RecursiveTraversalState.Enumerating, rootPending.RootTraversalState);
            Assert.False(rootPending.RootListingComplete);
            Assert.Equal(RecursiveTraversalState.Pending, rootPending.GetNode(parent).TraversalState);
            Assert.False(rootPending.GetNode(parent).ListingComplete);
            AssertSafety(Project(rootPending), [rootFile], RecursiveOperationRisk.IncompleteIdentity, 1, 1);
            AssertSafety(Project(rootPending), [parent],
                RecursiveOperationRisk.IncompleteIdentity | RecursiveOperationRisk.IncompleteSubtree, 1, 1);

            rootRelease.TrySetResult();
            await parentWaiting.Task.WaitAsync(TestTimeout);
            var parentPending = session.CurrentSnapshot;
            var projection = Project(parentPending);
            var child = Id(parentPending, @"parent\child.txt");
            Assert.Equal(RecursiveSessionState.Loading, parentPending.State);
            Assert.Equal(RecursiveTraversalState.Completed, parentPending.RootTraversalState);
            Assert.True(parentPending.RootListingComplete);
            Assert.Equal(RecursiveTraversalState.Enumerating, parentPending.GetNode(parent).TraversalState);
            Assert.False(parentPending.GetNode(parent).ListingComplete);
            AssertSafety(projection, [rootFile], RecursiveOperationRisk.None, 1, 0);
            AssertSafety(projection, [parent], RecursiveOperationRisk.None, 1, 0, RecursiveOperationScope.IdentityOnly);
            AssertSafety(projection, [parent], RecursiveOperationRisk.IncompleteSubtree, 1, 1);
            AssertSafety(projection, [child], RecursiveOperationRisk.IncompleteIdentity, 1, 1);

            parentRelease.TrySetResult();
            await run.WaitAsync(TestTimeout);
            var final = session.CurrentSnapshot;
            Assert.True(final.RootListingComplete);
            Assert.True(final.GetNode(parent).ListingComplete);
            AssertSafety(Project(final), [parent, rootFile, child], RecursiveOperationRisk.None, 3, 0);
            Assert.False(rootPending.RootListingComplete);
            Assert.Equal(RecursiveTraversalState.Pending, rootPending.GetNode(parent).TraversalState);
            Assert.False(parentPending.GetNode(parent).ListingComplete);
            AssertSafety(projection, [child], RecursiveOperationRisk.IncompleteIdentity, 1, 1);
        }
        finally
        {
            rootRelease.TrySetResult();
            parentRelease.TrySetResult();
        }
    }

    [Fact]
    public async Task Completed_unrelated_branch_is_safe_while_another_branch_is_still_loading()
    {
        var waiting = Signal();
        var release = Signal();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (request.Path == Root) yield return Batch(request, [Directory("safe"), Directory("pending")]);
            else if (request.Path == Root + @"\safe") yield return Batch(request, [File("child.txt")]);
            else
            {
                await Task.Delay(40, token);
                yield return Batch(request, [File("early.txt")], final: false);
                waiting.TrySetResult();
                await release.Task.WaitAsync(token);
                yield return Batch(request, []);
            }
        }
        await using var session = Session(new ScriptedEnumerator { Read = Read },
            new() { ProgressInterval = TimeSpan.FromMilliseconds(16) });
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            var snapshot = session.CurrentSnapshot;
            var projection = Project(snapshot);
            Assert.Equal(RecursiveSessionState.Loading, snapshot.State);
            Assert.True(snapshot.RootListingComplete);
            Assert.True(snapshot.GetNode(Id(snapshot, "safe")).ListingComplete);
            AssertSafety(projection, [Id(snapshot, "safe"), Id(snapshot, @"safe\child.txt")],
                RecursiveOperationRisk.None, 2, 0);
            AssertSafety(projection, [Id(snapshot, "pending")], RecursiveOperationRisk.IncompleteSubtree, 1, 1);
            release.TrySetResult();
            await run.WaitAsync(TestTimeout);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Filter_context_and_invisible_rows_are_reported_as_invalid_selection_instead_of_dropped()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent"), File("unmatched.txt")]),
            (Root + @"\parent", [File("target.txt")])));
        var projection = Project(snapshot, filter: new() { Query = "target" });
        var parent = Id(snapshot, "parent");
        var target = Id(snapshot, @"parent\target.txt");
        var unmatched = Id(snapshot, "unmatched.txt");
        Assert.True(projection.IsContextOnly(parent));
        Assert.False(projection.IsVisible(unmatched));
        Assert.Equal(RecursiveOperationRisk.InvalidSelection, projection.GetOperationRisk(parent));
        Assert.Equal(RecursiveOperationRisk.InvalidSelection, projection.GetOperationRisk(unmatched));
        Assert.False(projection.IsOperationSafe(parent, RecursiveOperationScope.IdentityOnly));
        AssertSafety(projection, [parent, target, unmatched], RecursiveOperationRisk.InvalidSelection, 3, 2);
        AssertSafety(projection, [target], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Collapsed_descendants_and_out_of_range_ids_are_explicit_invalid_selections()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent")]), (Root + @"\parent", [File("child.txt")])));
        var parent = Id(snapshot, "parent");
        var child = Id(snapshot, @"parent\child.txt");
        var projection = Project(snapshot, collapsed: new HashSet<int> { parent });
        foreach (var id in new[] { child, -1, 0, snapshot.Count + 1, int.MaxValue })
        {
            Assert.Equal(RecursiveOperationRisk.InvalidSelection, projection.GetOperationRisk(id));
            Assert.False(projection.IsOperationSafe(id));
            AssertSafety(projection, [id], RecursiveOperationRisk.InvalidSelection, 1, 1);
        }
        AssertSafety(projection, [parent, child, -1, 0, snapshot.Count + 1, int.MaxValue],
            RecursiveOperationRisk.InvalidSelection, 6, 5);
    }

    [Fact]
    public async Task Empty_selection_is_explicitly_unsafe_in_both_scopes()
    {
        var projection = Project(await ScanAsync(Tree((Root, [File("safe.txt")]))));
        AssertSafety(projection, [], RecursiveOperationRisk.EmptySelection, 0, 0);
        AssertSafety(projection, [], RecursiveOperationRisk.EmptySelection, 0, 0, RecursiveOperationScope.IdentityOnly);
    }

    [Fact]
    public async Task Repeated_ids_are_counted_once_without_hiding_distinct_aliases_or_invalid_selections()
    {
        var snapshot = await ScanAsync(Tree((Root, [File("A"), File("a"), File("safe.txt")])));
        var projection = Project(snapshot);
        var upper = Id(snapshot, "A");
        var lower = Id(snapshot, "a");
        var safe = Id(snapshot, "safe.txt");
        AssertSafety(projection, [upper, upper, lower, lower, safe, safe, -1, -1],
            RecursiveOperationRisk.CaseFoldCollision | RecursiveOperationRisk.InvalidSelection, 4, 3);
        Assert.Equal(new[] { Root + @"\A", Root + @"\a" },
            projection.ResolveOperationPaths([upper, upper, lower, lower]));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Provider_attribute_exclusions_never_claim_complete_identity_from_emitted_rows_only(
        bool includeHidden, bool includeSystem)
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent"), File("root.txt")]),
            (Root + @"\parent", [File("child.txt")])), new()
            {
                DirectoryOptions = new() { IncludeHidden = includeHidden, IncludeSystem = includeSystem }
            });
        var projection = Project(snapshot);
        Assert.False(snapshot.CompleteEnumerationScope);
        Assert.False(snapshot.RootListingComplete);
        Assert.Equal(RecursiveTraversalState.Completed, snapshot.RootTraversalState);
        AssertSafety(projection, [Id(snapshot, "root.txt"), Id(snapshot, @"parent\child.txt")],
            RecursiveOperationRisk.IncompleteIdentity, 2, 2);
        AssertSafety(projection, [Id(snapshot, "parent")],
            RecursiveOperationRisk.IncompleteIdentity | RecursiveOperationRisk.IncompleteSubtree, 1, 1);
    }

    [Theory]
    [InlineData(FileAttributes.Hidden, false)]
    [InlineData(FileAttributes.Hidden, true)]
    [InlineData(FileAttributes.System, false)]
    public async Task Suppressed_ancestor_also_suppresses_ordinary_descendants_even_when_they_match_the_query(
        FileAttributes excludedAttribute, bool useHiddenFilter)
    {
        var snapshot = await ScanAsync(Tree((Root,
                [Directory("excluded", FileAttributes.Directory | excludedAttribute), Directory("shown")]),
            (Root + @"\excluded", [Directory("nested")]),
            (Root + @"\excluded\nested", [File("target.txt")]),
            (Root + @"\shown", [File("target.txt")])));
        var projection = snapshot.BuildProjection(EntrySort.Name,
            new() { Query = "target", IncludeHidden = !useHiddenFilter }, StringComparer.OrdinalIgnoreCase,
            excludedAttributes: useHiddenFilter ? 0 : excludedAttribute);
        Assert.Equal(new[] { "shown", @"shown\target.txt" }, Paths(projection));
        Assert.Equal(1, projection.MatchCount);
        foreach (var path in new[] { "excluded", @"excluded\nested", @"excluded\nested\target.txt" })
        {
            Assert.False(projection.IsVisible(Id(snapshot, path)));
            Assert.Equal(RecursiveOperationRisk.InvalidSelection, projection.GetOperationRisk(Id(snapshot, path)));
        }
        AssertSafety(projection, [Id(snapshot, @"shown\target.txt")], RecursiveOperationRisk.None, 1, 0);
    }

    [Theory]
    [InlineData(FileAttributes.Hidden, false)]
    [InlineData(FileAttributes.Hidden, true)]
    [InlineData(FileAttributes.System, false)]
    public async Task Attribute_only_suppression_preserves_ordinary_collapse_and_does_not_reveal_children(
        FileAttributes excludedAttribute, bool useHiddenFilter)
    {
        var snapshot = await ScanAsync(Tree((Root,
                [Directory("shown"), File("excluded.txt", attributes: excludedAttribute)]),
            (Root + @"\shown", [File("child.txt")])));
        var parent = Id(snapshot, "shown");
        var projection = snapshot.BuildProjection(EntrySort.Name,
            new() { IncludeHidden = !useHiddenFilter }, StringComparer.OrdinalIgnoreCase,
            new HashSet<int> { parent }, excludedAttributes: useHiddenFilter ? 0 : excludedAttribute);
        Assert.Equal(new[] { "shown" }, Paths(projection));
        Assert.Equal(2, projection.MatchCount);
        Assert.False(projection.IsFiltering);
        Assert.False(projection.IsExpanded(parent));
        Assert.True(projection.HasChildren(parent));
        Assert.False(projection.IsVisible(Id(snapshot, @"shown\child.txt")));
        AssertSafety(projection, [parent], RecursiveOperationRisk.None, 1, 0);
    }

    [Theory]
    [InlineData(FileAttributes.Hidden, false)]
    [InlineData(FileAttributes.Hidden, true)]
    [InlineData(FileAttributes.System, false)]
    public async Task Parent_with_only_attribute_suppressed_children_has_no_navigable_children(
        FileAttributes excludedAttribute, bool useHiddenFilter)
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent")]),
            (Root + @"\parent", [File("excluded.txt", attributes: excludedAttribute),
                Directory("excluded-directory", FileAttributes.Directory | excludedAttribute)]),
            (Root + @"\parent\excluded-directory", [File("ordinary-descendant.txt")])));
        var parent = Id(snapshot, "parent");
        var projection = snapshot.BuildProjection(EntrySort.Name,
            new() { IncludeHidden = !useHiddenFilter }, StringComparer.OrdinalIgnoreCase,
            excludedAttributes: useHiddenFilter ? 0 : excludedAttribute);
        Assert.Equal(new[] { "parent" }, Paths(projection));
        Assert.Equal(1, projection.MatchCount);
        Assert.True(projection.IsSelectable(parent));
        Assert.False(projection.HasChildren(parent));
        Assert.True(Project(snapshot).HasChildren(parent));
        AssertSafety(projection, [parent], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Query_matching_parent_with_only_unmatched_children_has_no_navigable_children()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("target-parent")]),
            (Root + @"\target-parent", [Directory("nested"), File("unmatched.txt")]),
            (Root + @"\target-parent\nested", [File("ordinary-descendant.txt")])));
        var parent = Id(snapshot, "target-parent");
        var projection = Project(snapshot, filter: new() { Query = "target" });
        Assert.Equal(new[] { "target-parent" }, Paths(projection));
        Assert.Equal(1, projection.MatchCount);
        Assert.True(projection.IsSelectable(parent));
        Assert.False(projection.HasChildren(parent));
        Assert.True(Project(snapshot).HasChildren(parent));
    }

    [Fact]
    public async Task Suppressing_collision_alias_and_failed_branch_never_removes_parent_subtree_risks()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => request.Path == Root
                ? Batches(Batch(request, [Directory("parent")]))
                : request.Path == Root + @"\parent"
                    ? Batches(Batch(request, [File("A"), File("a", attributes: FileAttributes.Hidden),
                        Directory("denied", FileAttributes.Directory | FileAttributes.Hidden), File("safe.txt")]))
                    : Failure(new UnauthorizedAccessException("fixture denied"))
        };
        var snapshot = await ScanAsync(enumerator);
        var projection = snapshot.BuildProjection(EntrySort.Name, EntryFilter.None, StringComparer.OrdinalIgnoreCase,
            excludedAttributes: FileAttributes.Hidden);
        var parent = Id(snapshot, "parent");
        Assert.Equal(new[] { "parent", @"parent\A", @"parent\safe.txt" }, Paths(projection));
        Assert.False(projection.IsVisible(Id(snapshot, @"parent\a")));
        Assert.False(projection.IsVisible(Id(snapshot, @"parent\denied")));
        AssertSafety(projection, [parent], RecursiveOperationRisk.CaseFoldCollision | RecursiveOperationRisk.IncompleteSubtree, 1, 1);
        AssertSafety(projection, [parent], RecursiveOperationRisk.None, 1, 0, RecursiveOperationScope.IdentityOnly);
        AssertSafety(projection, [Id(snapshot, @"parent\A")], RecursiveOperationRisk.CaseFoldCollision, 1, 1);
        AssertSafety(projection, [Id(snapshot, @"parent\safe.txt")], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Omitted_malformed_entries_make_the_containing_listing_incomplete_for_operation_identity()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent"), File("root.txt")]),
            (Root + @"\parent", [File("child.txt"), File("bad?.txt")])));
        var projection = Project(snapshot);
        var parent = Id(snapshot, "parent");
        Assert.Equal(1, snapshot.SkippedEntries);
        Assert.True(snapshot.RootListingComplete);
        Assert.False(snapshot.GetNode(parent).ListingComplete);
        AssertSafety(projection, [parent], RecursiveOperationRisk.IncompleteSubtree, 1, 1);
        AssertSafety(projection, [parent], RecursiveOperationRisk.None, 1, 0, RecursiveOperationScope.IdentityOnly);
        AssertSafety(projection, [Id(snapshot, @"parent\child.txt")], RecursiveOperationRisk.IncompleteIdentity, 1, 1);
        AssertSafety(projection, [Id(snapshot, "root.txt")], RecursiveOperationRisk.None, 1, 0);
    }

    [Fact]
    public async Task Safety_evaluation_honors_pre_cancelled_tokens_including_empty_selection()
    {
        var projection = Project(await ScanAsync(Tree((Root, [File("safe.txt")]))));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => projection.EvaluateOperationSafety([1],
            cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => projection.EvaluateOperationSafety([],
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Safety_evaluation_honors_cancellation_from_the_selection_source()
    {
        var projection = Project(await ScanAsync(Tree((Root, [File("one.txt"), File("two.txt")]))));
        using var cancellation = new CancellationTokenSource();
        IEnumerable<int> Selection()
        {
            yield return 1;
            cancellation.Cancel();
            yield return 2;
        }
        Assert.Throws<OperationCanceledException>(() => projection.EvaluateOperationSafety(Selection(),
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Collision_appended_in_a_later_node_block_does_not_mutate_old_snapshot_or_safety_projection()
    {
        var waiting = Signal();
        var release = Signal();
        var firstEntries = new[] { File("A") }.Concat(Enumerable.Range(1, 299).Select(i => File($"filler-{i}.txt"))).ToArray();
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request,
            [EnumeratorCancellation] CancellationToken token)
        {
            yield return Batch(request, firstEntries, final: false);
            waiting.TrySetResult();
            await release.Task.WaitAsync(token);
            yield return Batch(request, [File("a")]);
        }
        await using var session = Session(new ScriptedEnumerator { Read = Read });
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            var earlier = session.CurrentSnapshot;
            var earlierProjection = Project(earlier);
            var earlierPaths = Paths(earlierProjection);
            var upper = Id(earlier, "A");
            Assert.Equal(300, earlier.Count);
            Assert.False(earlier.GetNode(upper).HasCaseFoldCollision);
            AssertSafety(earlierProjection, [upper], RecursiveOperationRisk.IncompleteIdentity, 1, 1);
            release.TrySetResult();
            await run.WaitAsync(TestTimeout);
            var final = session.CurrentSnapshot;
            var lower = Id(final, "a");
            Assert.Equal(301, final.Count);
            Assert.True(final.GetNode(upper).HasCaseFoldCollision);
            Assert.True(final.GetNode(lower).HasCaseFoldCollision);
            Assert.True(final.RootListingComplete);
            AssertSafety(Project(final), [upper, lower], RecursiveOperationRisk.CaseFoldCollision, 2, 2);
            Assert.Equal(300, earlier.Count);
            Assert.Equal(earlierPaths, Paths(earlierProjection));
            Assert.False(earlier.GetNode(upper).HasCaseFoldCollision);
            Assert.False(earlier.RootListingComplete);
            AssertSafety(earlierProjection, [upper], RecursiveOperationRisk.IncompleteIdentity, 1, 1);
        }
        finally { release.TrySetResult(); }
    }

    private static void AssertSafety(RecursiveProjection projection, IEnumerable<int> selected,
        RecursiveOperationRisk risk, int selectedCount, int unsafeCount,
        RecursiveOperationScope scope = RecursiveOperationScope.Subtree)
    {
        var result = projection.EvaluateOperationSafety(selected, scope);
        Assert.Equal(risk, result.Risk);
        Assert.Equal(risk == RecursiveOperationRisk.None, result.IsSafe);
        Assert.Equal(selectedCount, result.SelectedCount);
        Assert.Equal(unsafeCount, result.UnsafeCount);
    }
}
