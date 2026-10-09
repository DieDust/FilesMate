using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveTraversalTests
{
    [Fact]
    public async Task Node_budget_truncates_explicitly_and_marks_queued_directories_unvisited()
    {
        var enumerator = Tree((Root, [Directory("A"), Directory("B"), File("kept.txt"), File("omitted.txt")]));
        var snapshot = await ScanAsync(enumerator, new() { MaximumNodes = 3 });
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.True(snapshot.LimitReached);
        Assert.Equal(RecursiveLimit.Nodes, snapshot.Limits);
        Assert.Equal(RecursiveTraversalState.SkippedNodeLimit, snapshot.GetNode(1).TraversalState);
        Assert.Equal(RecursiveTraversalState.SkippedNodeLimit, snapshot.GetNode(2).TraversalState);
        Assert.True(snapshot.GetNode(1).TraversalSkipped);
        Assert.True(snapshot.SkippedDirectories >= 2);
        Assert.Single(enumerator.Requests);
    }

    [Fact]
    public async Task Exactly_full_leaf_listing_is_complete_when_final_proves_no_entries_were_omitted()
    {
        var snapshot = await ScanAsync(Tree((Root, [File("a.txt"), File("b.txt")])), new() { MaximumNodes = 2 });
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.False(snapshot.LimitReached);
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public async Task Full_node_budget_still_detects_omitted_children_and_keeps_accepted_parent()
    {
        var enumerator = Tree((Root, [Directory("A")]), (Root + @"\A", [File("omitted.txt")]));
        var snapshot = await ScanAsync(enumerator, new() { MaximumNodes = 1 });
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(RecursiveLimit.Nodes, snapshot.Limits);
        Assert.Equal(RecursiveTraversalState.SkippedNodeLimit, snapshot.GetNode(1).TraversalState);
        Assert.Equal(Root + @"\A", snapshot.ResolvePath(1));
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(2, 4, 2)]
    public async Task Depth_limit_counts_entry_levels_below_root_and_is_explicit(int depth, int expectedCount, int expectedRequests)
    {
        var enumerator = Tree((Root, [Directory("A"), File("root.txt")]),
            (Root + @"\A", [Directory("B"), File("a.txt")]),
            (Root + @"\A\B", [File("b.txt")]));
        var snapshot = await ScanAsync(enumerator, new() { MaximumDepth = depth });
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(RecursiveLimit.Depth, snapshot.Limits);
        Assert.Equal(expectedCount, snapshot.Count);
        Assert.Equal(expectedRequests, enumerator.Requests.Count);
        var lastDirectory = Id(snapshot, depth == 1 ? "A" : @"A\B");
        Assert.Equal(RecursiveTraversalState.SkippedDepthLimit, snapshot.GetNode(lastDirectory).TraversalState);
        Assert.Equal(depth - 1, snapshot.GetNode(lastDirectory).Depth);
        Assert.True(snapshot.GetNode(lastDirectory).TraversalSkipped);
        Assert.Equal(1, snapshot.SkippedDirectories);
    }

    [Fact]
    public async Task Node_and_depth_limits_can_be_reported_together()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("A"), File("omitted.txt")])),
            new() { MaximumNodes = 1, MaximumDepth = 1 });
        Assert.Equal(RecursiveLimit.Nodes | RecursiveLimit.Depth, snapshot.Limits);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
    }

    [Fact]
    public async Task Reparse_and_offline_entries_are_visible_but_never_probed_or_traversed()
    {
        var probe = new Probe();
        var enumerator = Tree((Root, [Directory("junction", FileAttributes.Directory | FileAttributes.ReparsePoint),
            Directory("offline", FileAttributes.Directory | FileAttributes.Offline), Directory("ordinary"),
            File("link.txt", attributes: FileAttributes.ReparsePoint)]),
            (Root + @"\ordinary", [File("safe.txt")]));
        var snapshot = await ScanAsync(enumerator, probe: probe);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(5, snapshot.Count);
        Assert.Equal(2, snapshot.SkippedDirectories);
        Assert.Equal(new[] { Root, Root + @"\ordinary" }, probe.Paths);
        Assert.Equal(new[] { Root, Root + @"\ordinary" }, enumerator.Requests.Select(r => r.Path));
        Assert.Equal(RecursiveTraversalState.SkippedReparsePoint, snapshot.GetNode(Id(snapshot, "junction")).TraversalState);
        Assert.True(snapshot.GetNode(Id(snapshot, "junction")).IsReparsePoint);
        Assert.Equal(RecursiveTraversalState.SkippedOffline, snapshot.GetNode(Id(snapshot, "offline")).TraversalState);
        Assert.Equal(RecursiveTraversalState.NotDirectory, snapshot.GetNode(Id(snapshot, "link.txt")).TraversalState);
        Assert.False(snapshot.LimitReached);
    }

    [Theory]
    [InlineData(FileAttributes.ReparsePoint, RecursiveTraversalState.SkippedReparsePoint)]
    [InlineData(FileAttributes.Offline, RecursiveTraversalState.SkippedOffline)]
    public async Task Directory_probe_recheck_prevents_traversal_after_entry_changes(FileAttributes changed,
        RecursiveTraversalState expected)
    {
        var enumerator = Tree((Root, [Directory("changed"), Directory("safe")]), (Root + @"\safe", [File("kept.txt")]));
        var probe = new Probe { Read = path => FileAttributes.Directory | (path.EndsWith(@"\changed") ? changed : 0) };
        var snapshot = await ScanAsync(enumerator, probe: probe);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(expected, snapshot.GetNode(Id(snapshot, "changed")).TraversalState);
        Assert.DoesNotContain(enumerator.Requests, r => r.Path.EndsWith(@"\changed"));
        Assert.Equal(1, snapshot.SkippedDirectories);
        if (changed == FileAttributes.ReparsePoint)
            Assert.True(snapshot.GetNode(Id(snapshot, "changed")).IsReparsePoint);
    }

    [Theory]
    [InlineData(FileAttributes.Directory | FileAttributes.ReparsePoint)]
    [InlineData(FileAttributes.Directory | FileAttributes.Offline)]
    [InlineData(FileAttributes.Normal)]
    public async Task Unavailable_or_excluded_root_fails_without_enumeration(FileAttributes attributes)
    {
        var enumerator = Tree((Root, [File("never.txt")]));
        var snapshot = await ScanAsync(enumerator, probe: new() { Read = _ => attributes });
        Assert.Equal(RecursiveSessionState.Failed, snapshot.State);
        Assert.Empty(snapshot.Entries);
        Assert.Empty(enumerator.Requests);
        Assert.NotNull(snapshot.Error);
        Assert.True(snapshot.Error.IsTerminal);
    }

    [Fact]
    public async Task Inaccessible_branch_keeps_its_row_and_continues_later_siblings()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => request.Path == Root
                ? Batches(Batch(request, [Directory("denied"), Directory("safe")]))
                : request.Path.EndsWith(@"\denied") ? Failure(new UnauthorizedAccessException("fixture denied"))
                : Batches(Batch(request, [File("kept.txt")]))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(1, snapshot.SkippedDirectories);
        Assert.Equal(RecursiveTraversalState.Failed, snapshot.GetNode(Id(snapshot, "denied")).TraversalState);
        Assert.Equal(RecursiveTraversalState.Completed, snapshot.GetNode(Id(snapshot, "safe")).TraversalState);
        Assert.Equal(DirectoryReadErrorKind.AccessDenied, snapshot.Error?.Kind);
        Assert.False(snapshot.Error?.IsTerminal);
        Assert.Equal(3, enumerator.DisposeCount);
    }

    [Fact]
    public async Task Root_enumeration_error_retains_accepted_rows_but_stops_queued_children()
    {
        var error = new DirectoryReadError(DirectoryReadErrorKind.AccessDenied, 5, "fixture denied", true);
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => Batches(Batch(request, [Directory("A"), File("kept.txt")], error: error))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Failed, snapshot.State);
        Assert.Equal(2, snapshot.Count);
        Assert.Same(error, snapshot.Error);
        Assert.Equal(RecursiveTraversalState.Failed, snapshot.GetNode(Id(snapshot, "A")).TraversalState);
        Assert.True(snapshot.GetNode(Id(snapshot, "A")).TraversalSkipped);
        Assert.Single(enumerator.Requests);
    }

    [Fact]
    public async Task Root_probe_exception_reports_terminal_failure_without_any_provider_call()
    {
        var enumerator = Tree((Root, []));
        var snapshot = await ScanAsync(enumerator,
            probe: new() { Read = _ => throw new DirectoryNotFoundException("fixture disappeared") });
        Assert.Equal(RecursiveSessionState.Failed, snapshot.State);
        Assert.Equal(DirectoryReadErrorKind.NotFound, snapshot.Error?.Kind);
        Assert.True(snapshot.Error?.IsTerminal);
        Assert.Empty(enumerator.Requests);
    }

    [Fact]
    public async Task Branch_without_final_marker_is_partial_and_sibling_enumeration_continues()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => request.Path == Root
                ? Batches(Batch(request, [Directory("incomplete"), Directory("safe")]))
                : Batches(Batch(request, [File("kept.txt")], final: !request.Path.EndsWith(@"\incomplete")))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(4, snapshot.Count);
        Assert.Equal(1, snapshot.SkippedDirectories);
        Assert.Equal(DirectoryReadErrorKind.PartialEnumeration, snapshot.Error?.Kind);
        Assert.Equal(RecursiveTraversalState.Failed, snapshot.GetNode(Id(snapshot, "incomplete")).TraversalState);
        Assert.Equal(RecursiveTraversalState.Completed, snapshot.GetNode(Id(snapshot, "safe")).TraversalState);
    }

    [Fact]
    public async Task Invalid_or_repeated_names_are_skipped_without_escaping_root_or_corrupting_identity()
    {
        var enumerator = new ScriptedEnumerator
        {
            Read = (request, _) => Batches(
                Batch(request, [File("good.txt"), File(".."), File("."), File(@"A\escape"), File("A/escape"),
                    File("C:escape"), File(" "), File("placeholder") with { Name = "bad\0name" }], final: false),
                Batch(request, [File("good.txt"), File("second.txt")]))
        };
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(8, snapshot.SkippedEntries);
        Assert.Equal(new[] { "good.txt", "second.txt" }, snapshot.Entries.Select(e => e.Name));
        Assert.Equal(new[] { 1, 2 }, snapshot.Entries.Select(e => e.Id));
        Assert.Equal(DirectoryReadErrorKind.PartialEnumeration, snapshot.Error?.Kind);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("//server/share")]
    [InlineData(@"C:relative")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\?\UNC\server\share")]
    [InlineData(@"D:\fixture\..\outside")]
    [InlineData(@"D:\fixture\.\child")]
    [InlineData("/fixture/../outside")]
    [InlineData("/fixture/./child")]
    public void Root_must_be_absolute_local_and_pre_normalized(string root)
    {
        Assert.Throws<ArgumentException>(() => Session(Tree((Root, [])), root: root));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\?\C:\fixture")]
    [InlineData("/fixture")]
    [InlineData("/")]
    public async Task Supported_local_roots_keep_path_resolution_inside_their_root(string root)
    {
        var snapshot = await ScanAsync(Tree((root, [File("kept.txt")])), root: root);
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        var separator = root.Contains('\\') ? "\\" : "/";
        Assert.Equal(root.TrimEnd('/', '\\') + separator + "kept.txt", snapshot.ResolvePath(1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2_000_001, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 257)]
    public void Invalid_node_or_depth_budgets_are_rejected(int nodes, int depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Session(Tree((Root, [])),
            new() { MaximumNodes = nodes, MaximumDepth = depth }));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(10_001)]
    public void Invalid_progress_interval_is_rejected(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Session(Tree((Root, [])),
            new() { ProgressInterval = TimeSpan.FromMilliseconds(milliseconds) }));
    }
    [Fact]
    public async Task Production_probe_rejects_a_root_beneath_a_symbolic_link_ancestor()
    {
        // Windows link creation needs account/developer-mode privileges. This Linux fixture verifies
        // the production probe, without touching user data or relaxing the Windows App test block.
        if (OperatingSystem.IsWindows()) return;
        var fixture = Path.Combine(AppContext.BaseDirectory, "artifacts", "recursive-probe-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(fixture, "target");
        var link = Path.Combine(fixture, "link");
        var child = Path.Combine(target, "child");
        var createdLink = false;
        try
        {
            System.IO.Directory.CreateDirectory(child);
            System.IO.Directory.CreateSymbolicLink(link, target);
            createdLink = true;
            var linkedChild = Path.Combine(link, "child");
            var enumerator = Tree((linkedChild, [File("must-not-enumerate.txt")]));
            await using var session = new RecursiveSession(enumerator, FilesMate.Core.Navigation.PaneId.New(), 1, linkedChild);
            await session.RunAsync().WaitAsync(TestTimeout);
            Assert.Equal(RecursiveSessionState.Failed, session.State);
            Assert.Equal(DirectoryReadErrorKind.UnsupportedLocation, session.CurrentSnapshot.Error?.Kind);
            Assert.Empty(enumerator.Requests);
            Assert.Empty(session.CurrentSnapshot.Entries);
        }
        finally
        {
            if (createdLink) System.IO.Directory.Delete(link);
            if (System.IO.Directory.Exists(fixture)) System.IO.Directory.Delete(fixture, recursive: true);
        }
    }

    [Theory]
    [InlineData(".. ")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("CON .txt")]
    [InlineData("PRN.json")]
    [InlineData("aux.zip")]
    [InlineData("NUL.tar.gz")]
    [InlineData("COM1.txt")]
    [InlineData("lpt9.txt")]
    [InlineData("COM¹.txt")]
    [InlineData("LPT².txt")]
    [InlineData("LPT³")]
    [InlineData("bad?.txt")]
    [InlineData("bad*.txt")]
    [InlineData("bad<.txt")]
    [InlineData("bad>.txt")]
    [InlineData("bad|.txt")]
    [InlineData("bad\".txt")]
    [InlineData("bad\u0001.txt")]
    public async Task Ambiguous_windows_names_are_skipped_before_they_can_form_operation_targets(string name)
    {
        var enumerator = Tree((Root, [Directory(name), File("safe.txt")]));
        var snapshot = await ScanAsync(enumerator);
        Assert.Equal(RecursiveSessionState.Partial, snapshot.State);
        Assert.Equal(1, snapshot.SkippedEntries);
        Assert.Equal("safe.txt", Assert.Single(snapshot.Entries).Name);
        Assert.Equal(1, snapshot.GetEntry(1).Id);
        Assert.Equal(DirectoryReadErrorKind.PartialEnumeration, snapshot.Error?.Kind);
        Assert.Single(enumerator.Requests);
        Assert.Equal(new[] { Root + @"\safe.txt" }, Project(snapshot).ResolveOperationPaths([1]));
    }

    [Theory]
    [InlineData(@"C:\ordinary\.. ")]
    [InlineData(@"C:\ordinary.")]
    [InlineData(@"C:\ordinary ")]
    [InlineData(@"C:\NUL.txt\child")]
    [InlineData(@"C:\COM1\child")]
    [InlineData(@"C:\ordinary?\child")]
    [InlineData(@"\\?\C:\ordinary\.. ")]
    public void Ambiguous_windows_root_components_are_rejected_before_probing(string root)
    {
        var probe = new Probe();
        var enumerator = Tree((root, []));
        Assert.Throws<ArgumentException>(() => Session(enumerator, probe: probe, root: root));
        Assert.Empty(probe.Paths);
        Assert.Empty(enumerator.Requests);
    }

    [Theory]
    [InlineData(".. ")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("CON.txt")]
    [InlineData("COM1")]
    [InlineData("file?.txt")]
    [InlineData("file*.txt")]
    public async Task Windows_alias_restrictions_do_not_rewrite_or_omit_valid_unix_file_names(string name)
    {
        const string root = "/recursive-fixture";
        var snapshot = await ScanAsync(Tree((root, [File(name)])), root: root);
        Assert.Equal(RecursiveSessionState.Completed, snapshot.State);
        Assert.Equal(0, snapshot.SkippedEntries);
        Assert.Equal(name, Assert.Single(snapshot.Entries).Name);
        Assert.Equal(root + "/" + name, snapshot.ResolvePath(1));
    }

}
