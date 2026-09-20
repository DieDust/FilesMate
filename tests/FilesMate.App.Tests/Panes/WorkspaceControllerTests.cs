using FilesMate.App.Workspace;
using FilesMate.Core.Navigation;

namespace FilesMate.App.Tests.Panes;

public sealed class WorkspaceControllerTests
{
    [Fact]
    public async Task Secondary_pane_is_lazy_and_layout_is_independent()
    {
        var created = 0;
        await using var workspace = new WorkspaceController(() =>
        {
            created++;
            return new PaneSession(PaneId.New());
        });

        Assert.Equal(1, created);
        Assert.False(workspace.IsDualPane);

        workspace.SetLayout(WorkspaceLayoutKind.Vertical);

        Assert.Equal(2, created);
        Assert.True(workspace.IsDualPane);
        Assert.NotNull(workspace.Right);
        Assert.Same(workspace.Left, workspace.ActivePane);
    }

    [Fact]
    public async Task Active_pane_and_split_ratio_are_routed_and_clamped()
    {
        await using var workspace = new WorkspaceController(() => new PaneSession(PaneId.New()));
        var changes = 0;
        workspace.StateChanged += (_, _) => changes++;
        workspace.SetLayout(WorkspaceLayoutKind.Horizontal);
        workspace.SetActivePane(workspace.Right!);
        workspace.SetSplitRatio(0.99);

        Assert.Same(workspace.Right, workspace.ActivePane);
        Assert.Equal(0.8, workspace.State.SplitRatio);
        Assert.Equal(3, changes);

        workspace.ResetSplitRatio();
        Assert.Equal(0.5, workspace.State.SplitRatio);
    }

    [Fact]
    public void State_normalization_removes_secondary_path_in_single_layout()
    {
        var state = new WorkspaceState
        {
            Layout = WorkspaceLayoutKind.Single,
            SplitRatio = double.NaN,
            RightPath = @"C:\Other",
        }.Normalize();

        Assert.Equal(0.5, state.SplitRatio);
        Assert.Null(state.RightPath);
    }
}
