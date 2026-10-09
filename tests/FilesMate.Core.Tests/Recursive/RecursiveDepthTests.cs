using FilesMate.Core.Entries;
using FilesMate.Core.Recursive;
using FilesMate.Core.Operations;
using FilesMate.App.Models;
using FilesMate.App.Services;
using static FilesMate.Core.Tests.Recursive.RecursiveTestData;

namespace FilesMate.Core.Tests.Recursive;

public sealed class RecursiveDepthTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(int.MaxValue, 8)]
    public async Task Directory_scope_depth_shows_exact_descendant_levels(int depth, int expectedVisibleDepth)
    {
        var snapshot = await DeepAsync(); var state = new RecursiveExpansionState();
        var scope = Id(snapshot, "A");
        state.SetDepthWithUndo(snapshot, scope, depth);
        var projection = ProjectDepth(snapshot, state);
        Assert.Equal(expectedVisibleDepth, projection.MaximumVisibleDepth);
        Assert.False(projection.IsExpanded(Id(snapshot, "B")));
        Assert.Contains("B", Paths(projection));
        Assert.DoesNotContain(@"B\other.txt", Paths(projection));
        foreach (var id in Enumerable.Range(1,snapshot.Count))
            if (projection.IsVisible(id)) Assert.Equal(id, projection.Index.GetEntry(projection.Store, projection.ViewIndexOfId(id)).Id);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, 1)] [InlineData(5, 4)] [InlineData(int.MaxValue, 8)]
    public async Task Root_children_are_level_one_and_grandchildren_level_two(int depth, int maximum)
    {
        var snapshot=await DeepAsync();var state=new RecursiveExpansionState();
        state.SetDepthWithUndo(snapshot,0,depth);
        var view=ProjectDepth(snapshot,state); Assert.Equal(maximum,view.MaximumVisibleDepth);
        if(depth==2){Assert.Contains(@"A\child",Paths(view));Assert.DoesNotContain(@"A\child\child",Paths(view));}
        Assert.Equal(depth==int.MaxValue,state.IsSubtreeExpanded(snapshot,0));
    }

    [Fact]
    public async Task Depth_overrides_preserve_exact_sequence_on_multi_scope_undo_and_cancel()
    {
        var snapshot=await DeepAsync();var state=new RecursiveExpansionState();
        var a=Id(snapshot,"A");var b=Id(snapshot,"B");
        state.SetExpanded(0,true,subtree:true); var original=ProjectDepth(snapshot,state).Index.ToArray();
        var changes=new[]{state.SetDepthWithUndo(snapshot,a,2),state.SetDepthWithUndo(snapshot,b,0)};
        var limited=ProjectDepth(snapshot,state).Index.ToArray(); Assert.NotEqual(original.Length,limited.Length);
        foreach(var change in changes.Reverse())state.RestoreIntent(change.EntryId,change.Subtree,change.Before);
        Assert.Equal(original,ProjectDepth(snapshot,state).Index.ToArray());
        foreach(var change in changes)state.RestoreIntent(change.EntryId,change.Subtree,change.After);
        Assert.Equal(limited,ProjectDepth(snapshot,state).Index.ToArray());
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>snapshot.BuildProjection(EntrySort.Name,EntryFilter.None,StringComparer.Ordinal,expansionPlan:state.Capture(),cancellationToken:cancelled.Token));
        foreach(var change in changes.Reverse())state.RestoreIntent(change.EntryId,change.Subtree,change.Before);
        Assert.Equal(original,ProjectDepth(snapshot,state).Index.ToArray());
        Assert.Throws<ArgumentException>(()=>state.SetDepthWithUndo(snapshot,Id(snapshot,@"B\other.txt"),1));
    }

    [Fact]
    public void Cancelling_exact_provisional_view_record_keeps_later_file_history_order()
    {
        var history=new FileUndoStack();var older=FileUndoRecord.View(new NoopView());history.Push(older);
        var pending=FileUndoRecord.View(new NoopView());history.Push(pending);
        var later=new FileUndoRecord(FileUndoKind.Relocated,[],[]);history.Push(later);
        Assert.True(history.TryDiscardPendingView(pending));
        Assert.Same(later,history.Latest);Assert.Equal(new[]{later,older},history.UndoRecords);
        Assert.False(history.TryDiscardPendingView(later));Assert.False(history.TryDiscardPendingView(pending));
    }

    [Fact]
    public async Task Settings_missing_invalid_and_roundtrip_keep_legacy_and_click_preferences()
    {
        var folder=Path.Combine(Path.GetTempPath(),"FilesMate-depth-test-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(folder);
        try
        {
            var file=Path.Combine(folder,"explorer.json");var service=new ExplorerPreferencesService(file);
            Assert.Equal(0, service.Load().RecursiveAutoExpandDepth);
            await System.IO.File.WriteAllTextAsync(file,"{\"folderOpeningMode\":\"SingleClick\"}");
            Assert.Equal(0, service.Load().RecursiveAutoExpandDepth);Assert.Equal(ItemOpeningMode.SingleClick,service.Load().FolderOpeningMode);
            foreach(var depth in new[]{0,1,2,5,int.MaxValue})
            {
                await service.SaveAsync(ExplorerPreferences.Default with {RecursiveAutoExpandDepth=depth,FolderOpeningMode=ItemOpeningMode.NameClick});
                Assert.Equal(depth,service.Load().RecursiveAutoExpandDepth);Assert.Equal(ItemOpeningMode.NameClick,service.Load().FolderOpeningMode);
            }
            await System.IO.File.WriteAllTextAsync(file,"{\"recursiveAutoExpandDepth\":6}");Assert.Equal(0, service.Load().RecursiveAutoExpandDepth);
        }
        finally {System.IO.Directory.Delete(folder,true);}
    }

    [Fact]
    public async Task Future_arrivals_inherit_depth_and_different_session_rejects_old_publication()
    {
        var provider = Tree((Root, [Directory("A")]), (Root + @"\A", [Directory("child"),File("direct.txt")]),
            (Root + @"\A\child", [Directory("deep")]), (Root + @"\A\child\deep", [File("hidden.txt")]));
        await using var session = Session(provider, new() {MaximumNodes=2,AllowContinuation=true,AutomaticContinuation=true});
        var state = new RecursiveExpansionState();
        state.SetDepthWithUndo(session.CurrentSnapshot,0,2); // No node IDs existed when the root depth was captured.
        var captured = state.Capture();
        await session.RunAsync().WaitAsync(TestTimeout);
        Assert.Equal(RecursiveSessionState.Completed,session.State);
        Assert.Equal(5,session.CurrentSnapshot.Count);
        var first = session.CurrentSnapshot.BuildProjection(EntrySort.Name,EntryFilter.None,StringComparer.Ordinal,expansionPlan:captured);
        Assert.Contains(@"A\child",Paths(first));Assert.DoesNotContain(@"A\child\deep",Paths(first));
        await using var next = Session(Tree((Root,[File("new.txt")])),generation:8);
        await next.RunAsync().WaitAsync(TestTimeout);
        var nextProjection = ProjectDepth(next.CurrentSnapshot,new RecursiveExpansionState());
        Assert.False(first.Index.CanPublish(nextProjection.Store));Assert.False(first.Index.Matches(8,EntryFilter.None));
        Assert.Contains(@"A\child",Paths(first)); // A navigation replacement cannot mutate retained old rows/intentions.
    }

    [Fact]
    public async Task Outermost_multi_selection_preserves_unrelated_scopes_and_honors_cancellation()
    {
        var snapshot=await DeepAsync();var a=Id(snapshot,"A");var child=Id(snapshot,@"A\child");var b=Id(snapshot,"B");
        Assert.Equal(new[]{a,b}.Order(),RecursiveDepthScopes.Outermost(snapshot,new HashSet<int>{a,child,b}));
        Assert.Equal(new[]{0},RecursiveDepthScopes.Outermost(snapshot,new HashSet<int>{0,a,child,b}));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>RecursiveDepthScopes.Outermost(snapshot,new HashSet<int>{a,b},cancellation.Token));
        Assert.Throws<ArgumentException>(()=>RecursiveDepthScopes.Outermost(snapshot,new HashSet<int>{Id(snapshot,@"B\other.txt")}));
    }

    private sealed class NoopView:IViewUndoChange {public string Description=>"test";public void Apply(bool redo) { }}
    private static RecursiveProjection ProjectDepth(RecursiveSnapshot snapshot,RecursiveExpansionState state)=>snapshot.BuildProjection(EntrySort.Name,EntryFilter.None,StringComparer.Ordinal,expansionPlan:state.Capture());
    private static Task<RecursiveSnapshot> DeepAsync()
    {
        var branches=new List<(string Path,FileEntryCore[] Entries)>{(Root,[Directory("A"),Directory("B"),File("root.txt")]),(Root+@"\B",[File("other.txt")])};
        var path=Root+@"\A";
        for(var level=1;level<=7;level++){branches.Add((path,[Directory("child"),File($"level{level}.txt")]));path+=@"\child";}
        branches.Add((path,[File("bottom.txt")]));return ScanAsync(Tree(branches.ToArray()));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Alt_adds_exact_descendant_levels_without_expanding_other_branches(int depth)
    {
        var snapshot = await DeepAsync(); var state = new RecursiveExpansionState(); var a = Id(snapshot, "A");
        state.EnsureDepthWithUndo(snapshot, a, depth);
        var view = ProjectDepth(snapshot, state);
        Assert.Equal(depth, view.MaximumVisibleDepth);
        Assert.False(state.IsExpanded(snapshot, Id(snapshot, "B")));
        Assert.DoesNotContain(@"B\other.txt", Paths(view));
        Assert.False(state.IsSubtreeExpanded(snapshot, a));
    }

    [Fact]
    public async Task Alt_completes_collapsed_inside_range_and_preserves_manual_deeper_expansion()
    {
        var snapshot = await DeepAsync(); var state = new RecursiveExpansionState();
        var a = Id(snapshot, "A"); var child = Id(snapshot, @"A\child"); var grandchild = Id(snapshot, @"A\child\child");
        state.SetExpanded(a, true); state.SetExpanded(child, false); state.SetExpanded(grandchild, true);
        state.EnsureDepthWithUndo(snapshot, a, 2);
        Assert.True(state.IsExpanded(snapshot, child)); Assert.True(state.IsExpanded(snapshot, grandchild));
        var view = ProjectDepth(snapshot, state);
        Assert.Contains(@"A\child\child\level3.txt", Paths(view));
        Assert.True(view.MaximumVisibleDepth >= 3);
        Assert.Equal(state.IsExpanded(snapshot, grandchild), view.IsExpanded(grandchild));
    }

    [Fact]
    public async Task Alt_does_not_reopen_a_deeper_collapse_outside_requested_range()
    {
        var snapshot = await DeepAsync(); var state = new RecursiveExpansionState();
        var a = Id(snapshot, "A"); var deeper = Id(snapshot, @"A\child\child\child");
        state.SetDepthWithUndo(snapshot, a, 5); state.SetExpanded(deeper, false);
        state.EnsureDepthWithUndo(snapshot, a, 2);
        Assert.False(state.IsExpanded(snapshot, deeper));
        Assert.False(ProjectDepth(snapshot, state).IsExpanded(deeper));
        Assert.DoesNotContain(@"A\child\child\child\level4.txt", Paths(ProjectDepth(snapshot, state)));
    }

    [Fact]
    public async Task Alt_undo_redo_preserves_exact_prior_projection_and_repeated_input_is_noop()
    {
        var snapshot = await DeepAsync(); var state = new RecursiveExpansionState(); var a = Id(snapshot, "A");
        state.SetDepthWithUndo(snapshot, a, 1); var original = ProjectDepth(snapshot, state).Index.ToArray();
        var change = state.EnsureDepthWithUndo(snapshot, a, 3); var expanded = ProjectDepth(snapshot, state).Index.ToArray();
        var repeat = state.EnsureDepthWithUndo(snapshot, a, 3);
        Assert.Equal(repeat.Before, repeat.After);
        state.RestoreIntent(a, true, change.Before); Assert.Equal(original, ProjectDepth(snapshot, state).Index.ToArray());
        state.RestoreIntent(a, true, change.After); Assert.Equal(expanded, ProjectDepth(snapshot, state).Index.ToArray());
        state.SetExpanded(a, false); Assert.DoesNotContain(@"A\child", Paths(ProjectDepth(snapshot, state)));
        state.EnsureDepthWithUndo(snapshot, a, 3); Assert.Contains(@"A\child\child\child", Paths(ProjectDepth(snapshot, state)));
    }

    [Fact]
    public async Task Alt_capture_keeps_future_arrivals_bounded_and_old_plan_immutable()
    {
        var provider = Tree((Root, [Directory("A")]), (Root + @"\A", [Directory("child"), File("direct.txt")]),
            (Root + @"\A\child", [Directory("deep")]), (Root + @"\A\child\deep", [File("hidden.txt")]));
        await using var session = Session(provider, new() { MaximumNodes = 2, AllowContinuation = true, AutomaticContinuation = true });
        var state = new RecursiveExpansionState(); state.EnsureDepthWithUndo(session.CurrentSnapshot, 0, 2); var captured = state.Capture();
        await session.RunAsync().WaitAsync(TestTimeout); var snapshot = session.CurrentSnapshot;
        state.EnsureDepthWithUndo(snapshot, 0, 3);
        var prior = snapshot.BuildProjection(EntrySort.Name, EntryFilter.None, StringComparer.Ordinal, expansionPlan: captured);
        Assert.Contains(@"A\child", Paths(prior)); Assert.DoesNotContain(@"A\child\deep", Paths(prior));
        Assert.Contains(@"A\child\deep", Paths(ProjectDepth(snapshot, state)));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => snapshot.BuildProjection(EntrySort.Name, EntryFilter.None, StringComparer.Ordinal, expansionPlan: captured, cancellationToken: cancelled.Token));
    }

    [Theory]
    [InlineData(0)] [InlineData(6)] [InlineData(int.MaxValue - 1)]
    public async Task Alt_rejects_depth_outside_one_through_five_or_all(int invalid)
    {
        var snapshot = await DeepAsync(); var state = new RecursiveExpansionState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.EnsureDepthWithUndo(snapshot, Id(snapshot, "A"), invalid));
        Assert.Throws<ArgumentException>(() => state.EnsureDepthWithUndo(snapshot, Id(snapshot, @"B\other.txt"), 2));
    }

    [Fact]
    public async Task Alt_setting_defaults_and_roundtrips_without_changing_ordinary_scope_or_opening()
    {
        var folder = Path.Combine(Path.GetTempPath(), "FilesMate-alt-depth-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "explorer.json"); var service = new ExplorerPreferencesService(file);
            Assert.Equal(2, service.Load().RecursiveAltExpandDepth);
            await System.IO.File.WriteAllTextAsync(file, "{\"recursiveAutoExpandDepth\":3,\"folderOpeningMode\":\"DoubleClick\",\"globalSearchScope\":true}");
            Assert.Equal(2, service.Load().RecursiveAltExpandDepth); Assert.Equal(3, service.Load().RecursiveAutoExpandDepth);
            foreach (var depth in new[] { 1, 2, 3, 4, 5, int.MaxValue })
            {
                await service.SaveAsync(service.Load() with { RecursiveAltExpandDepth = depth });
                var restored = service.Load(); Assert.Equal(depth, restored.RecursiveAltExpandDepth); Assert.Equal(3, restored.RecursiveAutoExpandDepth);
                Assert.True(restored.GlobalSearchScope); Assert.Equal(ItemOpeningMode.DoubleClick, restored.FolderOpeningMode);
            }
            await System.IO.File.WriteAllTextAsync(file, "{\"recursiveAltExpandDepth\":0,\"recursiveAutoExpandDepth\":5}");
            Assert.Equal(2, service.Load().RecursiveAltExpandDepth); Assert.Equal(5, service.Load().RecursiveAutoExpandDepth);
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }
}
