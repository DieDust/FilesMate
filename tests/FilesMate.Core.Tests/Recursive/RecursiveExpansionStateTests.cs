using FilesMate.Core.Entries;
using FilesMate.Core.Directories;
using System.Runtime.CompilerServices;
using FilesMate.Core.Recursive;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveExpansionStateTests
{
    [Fact]
    public async Task Subtree_menu_requires_descendant_intent_and_tracks_later_overrides()
    {
        var snapshot = await SampleAsync();
        var a = Id(snapshot, "A"); var nested = Id(snapshot, @"A\nested"); var b = Id(snapshot, "B");
        var state = new RecursiveExpansionState();
        state.SetExpanded(a, true);
        Assert.True(state.IsExpanded(snapshot, a));
        Assert.False(state.IsSubtreeExpanded(snapshot, a));
        state.SetExpanded(a, true, subtree: true);
        Assert.True(state.IsSubtreeExpanded(snapshot, a));
        Assert.False(state.IsSubtreeExpanded(snapshot, b));
        state.SetExpanded(nested, false);
        Assert.False(state.IsSubtreeExpanded(snapshot, a));
        state.SetExpanded(nested, true, subtree: true);
        Assert.True(state.IsSubtreeExpanded(snapshot, a));
        state.SetExpanded(0, true, subtree: true);
        Assert.True(state.IsSubtreeExpanded(snapshot, 0));
        state.SetExpanded(b, false, subtree: true);
        Assert.False(state.IsSubtreeExpanded(snapshot, 0));
        Assert.True(state.IsSubtreeExpanded(snapshot, a));
    }

    private static Task<RecursiveSnapshot> SampleAsync() => ScanAsync(Tree(
        (Root, [Directory("A"), Directory("B"), File("root.txt")]),
        (Root + @"\A", [Directory("nested"), File("a.txt")]),
        (Root + @"\A\nested", [File("target.txt")]),
        (Root + @"\B", [Directory("child")]),
        (Root + @"\B\child", [File("b.txt")])));

    private static RecursiveProjection Desired(RecursiveSnapshot snapshot, RecursiveExpansionState state, EntryFilter? filter = null) =>
        snapshot.BuildProjection(EntrySort.Name, filter ?? EntryFilter.None, StringComparer.OrdinalIgnoreCase, expansionPlan: state.Capture());

    [Fact]
    public async Task First_publication_is_collapsed_and_single_expand_reveals_one_level()
    {
        var snapshot = await SampleAsync();
        var state = new RecursiveExpansionState();
        var initial = Desired(snapshot, state);
        Assert.Equal(new[] { "root.txt", "A", "B" }, Paths(initial));
        state.SetExpanded(Id(snapshot, "A"), true);
        var expanded = initial.RebuildExpansion(state.Capture());
        Assert.Equal(new[] { "root.txt", "A", @"A\a.txt", @"A\nested", "B" }, Paths(expanded));
        Assert.True(initial.SharesStructureWith(expanded));
        Assert.Equal(new[] { "root.txt", "A", "B" }, Paths(initial));
        Assert.False(expanded.IsExpanded(Id(snapshot, @"A\nested")));
    }

    [Fact]
    public async Task Scoped_subtree_commands_and_latest_explicit_intent_preserve_other_branches()
    {
        var snapshot = await SampleAsync();
        var state = new RecursiveExpansionState();
        var a = Id(snapshot, "A"); var nested = Id(snapshot, @"A\nested");
        state.SetExpanded(a, true, subtree: true);
        var allA = Desired(snapshot, state);
        Assert.Contains(@"A\nested\target.txt", Paths(allA));
        Assert.DoesNotContain(@"B\child", Paths(allA));
        state.SetExpanded(nested, false);
        Assert.DoesNotContain(@"A\nested\target.txt", Paths(allA.RebuildExpansion(state.Capture())));
        state.SetExpanded(a, true, subtree: true);
        Assert.Contains(@"A\nested\target.txt", Paths(allA.RebuildExpansion(state.Capture())));
        state.SetExpanded(a, false, subtree: true);
        Assert.Equal(new[] { "root.txt", "A", "B" }, Paths(allA.RebuildExpansion(state.Capture())));
    }

    [Fact]
    public async Task Repeated_explicit_open_or_close_is_idempotent_and_captured_intents_are_immutable()
    {
        var snapshot = await SampleAsync(); var state = new RecursiveExpansionState(); var a = Id(snapshot, "A");
        state.SetExpanded(a, true); var open = state.Capture();
        for (var i = 0; i < 20; i++) state.SetExpanded(a, true);
        Assert.True(state.IsExpanded(snapshot, a));
        state.SetExpanded(a, false); var closed = state.Capture();
        for (var i = 0; i < 20; i++) state.SetExpanded(a, false);
        Assert.False(state.IsExpanded(snapshot, a));
        Assert.True(open.IsExpanded(snapshot, a)); Assert.False(closed.IsExpanded(snapshot, a));
        Assert.Equal(new[] { "root.txt", "A", @"A\a.txt", @"A\nested", "B" }, Paths(Desired(snapshot, state).RebuildExpansion(open)));
    }

    [Fact]
    public async Task Filter_still_reveals_ancestors_without_making_them_selectable()
    {
        var snapshot = await SampleAsync(); var state = new RecursiveExpansionState();
        var projection = Desired(snapshot, state, new EntryFilter { Query = "target" });
        Assert.Equal(new[] { "A", @"A\nested", @"A\nested\target.txt" }, Paths(projection));
        Assert.True(projection.IsFiltering); Assert.Equal(1, projection.MatchCount);
        Assert.False(projection.IsSelectable(Id(snapshot, "A")));
        Assert.True(projection.IsExpanded(Id(snapshot, "A")));
        Assert.False(state.IsExpanded(snapshot, Id(snapshot, "A")));
    }

    [Fact]
    public async Task Repeated_expansion_retains_case_collision_safety_and_exact_operation_paths()
    {
        var snapshot = await ScanAsync(Tree((Root, [Directory("parent")]),
            (Root + @"\parent", [File("A"), File("a"), File("safe.txt")])));
        var state = new RecursiveExpansionState(); var initial = Desired(snapshot, state); var parent = Id(snapshot, "parent");
        Assert.Equal(RecursiveOperationRisk.CaseFoldCollision, initial.GetOperationRisk(parent));
        state.SetExpanded(parent, true);
        var expanded = initial.RebuildExpansion(state.Capture());
        var upper = Id(snapshot, @"parent\A"); var lower = Id(snapshot, @"parent\a");
        Assert.Equal(RecursiveOperationRisk.CaseFoldCollision, expanded.GetOperationRisk(parent));
        Assert.False(expanded.IsOperationSafe(upper)); Assert.False(expanded.IsOperationSafe(lower));
        Assert.Equal(new[] { Root + @"\parent\A", Root + @"\parent\a" }, expanded.ResolveOperationPaths([upper, lower]));
        foreach (var id in Enumerable.Range(1, snapshot.Count))
            if (expanded.IsVisible(id)) Assert.Equal(id, expanded.Index.GetEntry(expanded.Store, expanded.ViewIndexOfId(id)).Id);
    }

    [Fact]
    public async Task Root_subtree_scope_can_be_overridden_and_clear_restores_default_collapse()
    {
        var snapshot = await SampleAsync(); var state = new RecursiveExpansionState();
        state.SetExpanded(0, true, subtree: true); var open = Desired(snapshot, state);
        Assert.Equal(snapshot.Count, open.Index.Count);
        state.SetExpanded(Id(snapshot, "A"), false, subtree: true);
        Assert.Contains(@"B\child\b.txt", Paths(open.RebuildExpansion(state.Capture())));
        state.Clear();
        Assert.Equal(new[] { "root.txt", "A", "B" }, Paths(open.RebuildExpansion(state.Capture())));
    }

    [Fact]
    public async Task Later_scan_arrivals_stay_collapsed_and_explicit_subtree_scope_includes_future_descendants()
    {
        var waiting = Signal(); var release = Signal();
        var publication = new TaskCompletionSource<RecursiveSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<DirectoryBatch> Read(DirectoryRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            if (request.Path == Root)
            {
                await Task.Delay(40, token);
                yield return Batch(request, [Directory("A"), File("first.txt")], final: false);
                waiting.TrySetResult(); await release.Task.WaitAsync(token);
                yield return Batch(request, [Directory("B")]);
            }
            else if (request.Path == Root + @"\A") yield return Batch(request, [Directory("nested")]);
            else if (request.Path == Root + @"\A\nested") yield return Batch(request, [File("target.txt")]);
            else yield return Batch(request, [File("b.txt")]);
        }
        await using var session = Session(new ScriptedEnumerator { Read = Read }, new() { ProgressInterval = TimeSpan.FromMilliseconds(16) });
        session.Changed += change => { if (change.Count == 2) publication.TrySetResult(change.Snapshot); };
        var run = session.RunAsync();
        try
        {
            await waiting.Task.WaitAsync(TestTimeout);
            var earlier = await publication.Task.WaitAsync(TestTimeout);
            var state = new RecursiveExpansionState(); var initial = Desired(earlier, state);
            Assert.Equal(new[] { "first.txt", "A" }, Paths(initial));
            Assert.False(initial.IsOperationSafe(Id(earlier, "A")));
            var defaultPlan = state.Capture();
            state.SetExpanded(Id(earlier, "A"), true, subtree: true); var openPlan = state.Capture();
            release.TrySetResult(); await run.WaitAsync(TestTimeout);
            var final = session.CurrentSnapshot;
            Assert.True(final.Revision > earlier.Revision);
            var stillClosed = final.BuildProjection(EntrySort.Name, EntryFilter.None, StringComparer.OrdinalIgnoreCase, expansionPlan: defaultPlan);
            Assert.Equal(new[] { "first.txt", "A", "B" }, Paths(stillClosed));
            Assert.False(initial.SharesStructureWith(stillClosed));
            var expanded = stillClosed.RebuildExpansion(openPlan);
            Assert.Equal(new[] { "first.txt", "A", @"A\nested", @"A\nested\target.txt", "B" }, Paths(expanded));
            Assert.Equal(new[] { "first.txt", "A" }, Paths(initial)); Assert.False(initial.IsOperationSafe(Id(earlier, "A")));
            Assert.Equal(Project(final).GetOperationRisk(Id(final, "A")), expanded.GetOperationRisk(Id(final, "A")));
        }
        finally { release.TrySetResult(); }
    }
}
