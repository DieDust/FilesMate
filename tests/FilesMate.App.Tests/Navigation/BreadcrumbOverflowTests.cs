using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class BreadcrumbOverflowTests
{
    [Fact]
    public void KeepsRootAndNearestAncestors()
    {
        var result = BreadcrumbOverflow.Fit([50, 90, 90, 90, 70], 280);
        Assert.True(result.ShowRoot);
        Assert.True(result.HasOverflow);
        Assert.Equal(3, result.SuffixStart);
        Assert.Equal(70, result.CurrentWidth);
    }

    [Fact]
    public void NarrowBarKeepsCurrentInsteadOfLongShareRoot()
    {
        var result = BreadcrumbOverflow.Fit([260, 90, 150], 180);
        Assert.False(result.ShowRoot);
        Assert.Equal(2, result.SuffixStart);
        Assert.Equal(144, result.CurrentWidth);
    }

    [Fact]
    public void FitsSingleLongNameWithoutEmptyOverflowMenu()
    {
        var result = BreadcrumbOverflow.Fit([500], 130);
        Assert.False(result.HasOverflow);
        Assert.Equal(130, result.CurrentWidth);
    }

    [Fact]
    public void ExpandingRestoresEveryAncestor()
    {
        var result = BreadcrumbOverflow.Fit([50, 90, 90, 90, 70], 390);
        Assert.False(result.HasOverflow);
        Assert.Equal(0, result.SuffixStart);
    }

    [Fact]
    public void EveryWidthKeepsCurrentAndPartitionsAllAncestors()
    {
        double[] widths = [75, 180, 45, 270, 80, 120, 300];
        for (var available = 80; available <= 1400; available++)
        {
            var result = BreadcrumbOverflow.Fit(widths, available);
            var used = result.CurrentWidth + (result.HasOverflow ? 36 : 0)
                + (result.ShowRoot ? widths[0] : 0)
                + widths.Skip(result.SuffixStart).Take(widths.Length - result.SuffixStart - 1).Sum();
            Assert.InRange(used, 0, available);
            Assert.InRange(result.CurrentWidth, 1, widths[^1]);
            if (result.HasOverflow) Assert.True(result.SuffixStart > (result.ShowRoot ? 1 : 0));
        }
    }
}
