using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class AlphabetNavigationPolicyTests
{
    [Theory]
    [InlineData(true, 19, 20, false, false, false)]
    [InlineData(true, 20, 20, false, false, true)]
    [InlineData(true, 100, 20, true, false, false)]
    [InlineData(true, 100, 20, true, true, true)]
    [InlineData(false, 100, 20, false, true, false)]
    public void Visibility_honors_count_and_dual_pane_policy(
        bool enabled,
        int itemCount,
        int minimum,
        bool isDualPane,
        bool showInDualPane,
        bool expected)
    {
        Assert.Equal(expected, AlphabetNavigationPolicy.ShouldShow(
            enabled, itemCount, minimum, isDualPane, showInDualPane));
    }

    [Theory]
    [InlineData(400, 28, 1)]
    [InlineData(399, 28, 2)]
    [InlineData(288, 28, 2)]
    [InlineData(520, 28, 1)]
    public void Layout_uses_one_column_only_when_every_letter_remains_readable(double height, int labels, int expected)
    {
        Assert.Equal(expected, AlphabetNavigationPolicy.ColumnCount(height, labels));
    }
}
