using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveProjectionTests
{
    private static Task<RecursiveSnapshot> SampleAsync() => ScanAsync(Tree(
        (Root, [Directory("B"), File("root.txt", 100), Directory("A")]),
        (Root + @"\B", [File("a.txt", 8), File("z.txt", 2)]),
        (Root + @"\A", [File("a.txt", 9), Directory("nested"), File("b.txt", 3)]),
        (Root + @"\A\nested", [File("target.txt", 5)])));

    [Fact]
    public async Task Duplicate_basenames_in_different_parents_have_distinct_stable_node_ids_and_paths()
    {
        var snapshot = await SampleAsync();
        var left = Id(snapshot, @"A\a.txt");
        var right = Id(snapshot, @"B\a.txt");
        Assert.NotEqual(left, right);
        Assert.NotEqual(snapshot.GetNode(left).ParentId, snapshot.GetNode(right).ParentId);
        Assert.Equal("a.txt", snapshot.GetEntry(left).Name);
        Assert.Equal("a.txt", snapshot.GetEntry(right).Name);
        Assert.Equal(Root + @"\A\a.txt", snapshot.ResolvePath(left));
        Assert.Equal(Root + @"\B\a.txt", snapshot.ResolvePath(right));
        Assert.Equal("A", snapshot.ResolveRelativeParentPath(left));
        Assert.Equal(string.Empty, snapshot.ResolveRelativeParentPath(Id(snapshot, "A")));
        Assert.Equal(Root, snapshot.ResolvePath(0));
        Assert.Equal(1, snapshot.GetNode(left).Depth);
        Assert.Equal(2, snapshot.GetNode(Id(snapshot, @"A\nested\target.txt")).Depth);
        Assert.Equal(Enumerable.Range(1, snapshot.Count), snapshot.Entries.Select(e => e.Id));
        foreach (var id in Enumerable.Range(1, snapshot.Count))
            Assert.True(snapshot.GetNode(id).ParentId < id);
    }

    [Fact]
    public async Task Name_sort_orders_siblings_and_keeps_each_tree_branch_contiguous()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot);
        Assert.Equal(new[] { "root.txt", "A", @"A\a.txt", @"A\b.txt", @"A\nested",
            @"A\nested\target.txt", "B", @"B\a.txt", @"B\z.txt" }, Paths(projection));
        Assert.Equal(snapshot.Count, projection.MatchCount);
        Assert.False(projection.IsFiltering);
        Assert.Equal(snapshot.SessionId, projection.SessionId);
        Assert.Equal(snapshot.Generation, projection.Generation);
        Assert.Equal(snapshot.Revision, projection.Revision);
        Assert.Equal(RecursiveSessionState.Completed, projection.State);
        Assert.Same(snapshot.Entries, projection.Index.Snapshot);
    }

    [Fact]
    public async Task Descending_size_orders_files_only_within_their_parent()
    {
        var projection = Project(await SampleAsync(), EntrySort.Size with { Ascending = false });
        Assert.Equal(new[] { "root.txt", "B", @"B\a.txt", @"B\z.txt", "A", @"A\a.txt", @"A\b.txt",
            @"A\nested", @"A\nested\target.txt" }, Paths(projection));
    }

    [Fact]
    public async Task Ascending_size_orders_files_only_within_their_parent()
    {
        var projection = Project(await SampleAsync(), EntrySort.Size);
        Assert.Equal(new[] { "root.txt", "A", @"A\b.txt", @"A\a.txt", @"A\nested",
            @"A\nested\target.txt", "B", @"B\z.txt", @"B\a.txt" }, Paths(projection));
    }

    [Theory]
    [InlineData(EntryGrouping.FilesFirst)]
    [InlineData(EntryGrouping.FoldersFirst)]
    [InlineData(EntryGrouping.Mixed)]
    public async Task Recursive_direct_files_precede_subtrees_independently_of_ordinary_grouping(EntryGrouping grouping)
    {
        var snapshot = await SampleAsync();
        var ordinarySort = EntrySort.Name with { Grouping = grouping };
        var filesFirst = Project(snapshot, ordinarySort);
        Assert.Equal(new[] { "root.txt", "A", @"A\a.txt", @"A\b.txt", @"A\nested",
            @"A\nested\target.txt", "B", @"B\a.txt", @"B\z.txt" }, Paths(filesFirst));
        var descending = Project(snapshot, ordinarySort with { Ascending = false });
        Assert.Equal(new[] { "root.txt", "B", @"B\z.txt", @"B\a.txt", "A", @"A\b.txt", @"A\a.txt",
            @"A\nested", @"A\nested\target.txt" }, Paths(descending));
        Assert.Equal(grouping, ordinarySort.EffectiveGrouping);
        Assert.Equal(EntryGrouping.FilesFirst, filesFirst.Index.Sort.EffectiveGrouping);
        Assert.Equal(RecursiveSnapshot.NormalizeTreeSort(ordinarySort), filesFirst.Index.Sort);
    }

    [Fact]
    public async Task Filtered_files_keep_direct_matches_before_context_subtrees()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot, filter: new() { FilesOnly = true },
            collapsed: new HashSet<int> { Id(snapshot, "A"), Id(snapshot, @"A\nested"), Id(snapshot, "B") });
        Assert.Equal(new[] { "root.txt", "A", @"A\a.txt", @"A\b.txt", @"A\nested",
            @"A\nested\target.txt", "B", @"B\a.txt", @"B\z.txt" }, Paths(projection));
        Assert.Equal(6, projection.MatchCount);
        foreach (var path in new[] { "A", @"A\nested", "B" })
        {
            Assert.True(projection.IsContextOnly(Id(snapshot, path)));
            Assert.False(projection.IsSelectable(Id(snapshot, path)));
        }
    }

    [Fact]
    public async Task Collapse_hides_descendants_without_changing_the_total_matching_count()
    {
        var snapshot = await SampleAsync();
        var a = Id(snapshot, "A");
        var collapsed = new HashSet<int> { a };
        var projection = Project(snapshot, collapsed: collapsed);
        collapsed.Clear();
        Assert.Equal(new[] { "root.txt", "A", "B", @"B\a.txt", @"B\z.txt" }, Paths(projection));
        Assert.Equal(snapshot.Count, projection.MatchCount);
        Assert.False(projection.IsExpanded(a));
        Assert.True(projection.HasChildren(a));
        Assert.True(projection.IsSelectable(a));
        Assert.False(projection.IsVisible(Id(snapshot, @"A\a.txt")));
        Assert.False(projection.IsSelectable(Id(snapshot, @"A\a.txt")));
        Assert.False(projection.IsVisible(0));
        Assert.False(projection.IsVisible(-1));
        Assert.False(projection.IsSelectable(snapshot.Count + 1));
    }

    [Fact]
    public async Task Filter_reveals_collapsed_ancestors_as_unselectable_context_without_counting_them()
    {
        var snapshot = await SampleAsync();
        var a = Id(snapshot, "A");
        var nested = Id(snapshot, @"A\nested");
        var target = Id(snapshot, @"A\nested\target.txt");
        var projection = Project(snapshot, filter: new() { Query = "target" }, collapsed: new HashSet<int> { a, nested });
        Assert.Equal(new[] { "A", @"A\nested", @"A\nested\target.txt" }, Paths(projection));
        Assert.Equal(1, projection.MatchCount);
        Assert.True(projection.IsFiltering);
        foreach (var ancestor in new[] { a, nested })
        {
            Assert.True(projection.IsVisible(ancestor));
            Assert.True(projection.IsContextOnly(ancestor));
            Assert.False(projection.IsSelectable(ancestor));
            Assert.True(projection.IsExpanded(ancestor));
        }
        Assert.True(projection.IsSelectable(target));
        Assert.False(projection.IsContextOnly(target));
        Assert.False(projection.HasChildren(target));
        Assert.False(projection.IsExpanded(target));
        Assert.Equal(new[] { Root + @"\A\nested\target.txt" }, projection.ResolveOperationPaths([a, nested, target]));
    }

    [Fact]
    public async Task Files_only_and_additional_predicate_both_preserve_ancestor_context()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot, filter: new() { FilesOnly = true }, additionalMatch: e => e.Size >= 8);
        Assert.Equal(new[] { "root.txt", "A", @"A\a.txt", "B", @"B\a.txt" }, Paths(projection));
        Assert.Equal(3, projection.MatchCount);
        Assert.True(projection.IsContextOnly(Id(snapshot, "A")));
        Assert.True(projection.IsContextOnly(Id(snapshot, "B")));
    }

    [Fact]
    public async Task Matching_directory_is_selectable_but_nonmatching_descendants_are_not_included()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot, filter: new() { Query = "nested" });
        Assert.Equal(new[] { "A", @"A\nested" }, Paths(projection));
        Assert.Equal(1, projection.MatchCount);
        Assert.True(projection.IsSelectable(Id(snapshot, @"A\nested")));
        Assert.False(projection.IsVisible(Id(snapshot, @"A\nested\target.txt")));
    }

    [Fact]
    public async Task Empty_filter_result_has_no_context_rows_or_selectable_nodes()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot, filter: new() { Query = "not-present" });
        Assert.Empty(projection.Index);
        Assert.Equal(0, projection.MatchCount);
        Assert.Empty(projection.ResolveOperationPaths(Enumerable.Range(1, snapshot.Count)));
    }

    [Fact]
    public async Task Captured_operations_deduplicate_ids_and_descendants_even_when_child_is_selected_first()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot);
        var a = Id(snapshot, "A");
        var aFile = Id(snapshot, @"A\a.txt");
        var nestedFile = Id(snapshot, @"A\nested\target.txt");
        var bFile = Id(snapshot, @"B\a.txt");
        Assert.Equal(new[] { Root + @"\B\a.txt", Root + @"\A" },
            projection.ResolveOperationPaths([nestedFile, aFile, bFile, a, a, bFile, 0, -1, snapshot.Count + 1]));
        Assert.Equal(new[] { Root + @"\A\a.txt", Root + @"\B\a.txt" },
            projection.ResolveOperationPaths([aFile, bFile]));
    }

    [Fact]
    public async Task Operation_selection_excludes_hidden_descendants()
    {
        var snapshot = await SampleAsync();
        var projection = Project(snapshot, collapsed: new HashSet<int> { Id(snapshot, "A") });
        Assert.Empty(projection.ResolveOperationPaths([Id(snapshot, @"A\a.txt"), Id(snapshot, @"A\nested")]));
    }

    [Fact]
    public async Task Projection_and_operation_capture_honor_pre_cancelled_tokens()
    {
        var snapshot = await SampleAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => snapshot.BuildProjection(EntrySort.Name,
            EntryFilter.None, StringComparer.Ordinal, cancellationToken: cancellation.Token));
        var projection = Project(snapshot);
        Assert.Throws<OperationCanceledException>(() => projection.ResolveOperationPaths([1], cancellation.Token));
    }

    [Theory]
    [InlineData(@"C:\A", @"C:\A\one.txt", true)]
    [InlineData(@"C:\A\", @"c:\a\nested\one.txt", true)]
    [InlineData(@"C:\A", @"C:\AB\one.txt", false)]
    [InlineData(@"C:\A", @"C:\A", false)]
    [InlineData(@"C:\A\", @"C:\A", false)]
    [InlineData(@"C:\A", @"D:\A\one.txt", false)]
    [InlineData(@"C:\A", "C:/A/one.txt", true)]
    [InlineData(@"\\?\C:\A", @"C:\A\one.txt", true)]
    [InlineData(@"\\server\share\A", @"\\server\share\AB\one.txt", false)]
    [InlineData(@"\\?\UNC\server\share\A", @"\\server\share\A\one.txt", true)]
    [InlineData("/fixture/A", "/fixture/A/one.txt", true)]
    [InlineData("/fixture/A", "/fixture/a/one.txt", false)]
    [InlineData("/fixture/A", "/fixture/AB/one.txt", false)]
    [InlineData("/", "/one.txt", true)]
    public void Operation_path_coverage_requires_a_real_separator_boundary(string parent, string child, bool expected)
    {
        Assert.Equal(expected, RecursiveOperationSelection.IsDescendantPath(parent, child));
    }

    [Fact]
    public async Task Node_access_rejects_virtual_root_and_out_of_bounds_ids()
    {
        var snapshot = await SampleAsync();
        foreach (var id in new[] { -1, 0, snapshot.Count + 1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetNode(id));
            Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.GetEntry(id));
            Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.ResolveRelativePath(id));
        }
    }
    [Fact]
    public async Task Detached_property_sort_keeps_siblings_together_without_changing_captured_paths()
    {
        var snapshot = await SampleAsync();
        var values = Enumerable.Range(1, snapshot.Count).ToDictionary(id => id,
            id => new EntryPropertyValue("fixture property", Number: snapshot.GetEntry(id).Size));
        var sort = EntrySort.Name with { Column = EntrySortColumn.ShellProperty, PropertyName = "Fixture.Value" };
        var projection = snapshot.BuildProjection(sort, EntryFilter.None, StringComparer.Ordinal,
            propertyValues: values);
        Assert.Equal(new[] { "root.txt", "A", @"A\b.txt", @"A\a.txt", @"A\nested",
            @"A\nested\target.txt", "B", @"B\z.txt", @"B\a.txt" }, Paths(projection));
        Assert.Equal(Root + @"\A\a.txt", projection.ResolvePath(Id(snapshot, @"A\a.txt")));
    }

    [Fact]
    public async Task Operation_capture_honors_cancellation_triggered_by_the_selection_source()
    {
        var projection = Project(await SampleAsync());
        using var cancellation = new CancellationTokenSource();
        IEnumerable<int> Selection()
        {
            yield return 1;
            cancellation.Cancel();
            yield return 2;
        }
        Assert.Throws<OperationCanceledException>(() => projection.ResolveOperationPaths(Selection(), cancellation.Token));
    }

}
