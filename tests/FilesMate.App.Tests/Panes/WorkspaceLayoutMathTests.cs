using FilesMate.App.Workspace;

namespace FilesMate.App.Tests.Panes;

public sealed class WorkspaceLayoutMathTests
{
    [Fact]
    public void Split_ratio_is_clamped_and_invalid_values_fall_back_to_center()
    {
        Assert.Equal(0.2, WorkspaceLayoutMath.ClampRatio(0.1));
        Assert.Equal(0.8, WorkspaceLayoutMath.ClampRatio(1.1));
        Assert.Equal(0.5, WorkspaceLayoutMath.ClampRatio(double.NaN));
        Assert.Equal(0.5, WorkspaceLayoutMath.ClampRatio(double.PositiveInfinity));
    }

    [Fact]
    public void Single_layout_hides_secondary_pane()
    {
        Assert.False(WorkspaceLayoutMath.IsSecondaryVisible(WorkspaceLayoutKind.Single));
        Assert.True(WorkspaceLayoutMath.IsSecondaryVisible(WorkspaceLayoutKind.Vertical));
        Assert.True(WorkspaceLayoutMath.IsSecondaryVisible(WorkspaceLayoutKind.Horizontal));
    }
}
