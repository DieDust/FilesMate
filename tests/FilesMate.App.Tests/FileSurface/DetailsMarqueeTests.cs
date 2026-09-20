using FilesMate.App.Controls.FileSurface;

namespace FilesMate.App.Tests.FileSurface;

public sealed class DetailsMarqueeTests
{
    [Theory]
    [InlineData(900, 1000)] // Right-side blank area, as in the reported screenshot.
    [InlineData(0, 12)] // Left padding.
    [InlineData(615, 700)] // Touches the right edge without overlapping.
    [InlineData(20, 20)] // No rectangle width.
    public void Blank_space_does_not_select_rows(double left, double right)
    {
        Assert.Empty(Select(left, 0, right, 280));
    }

    [Fact]
    public void Crossing_from_blank_space_into_rows_selects_only_intersections()
    {
        Assert.Equal(new[] { 1, 2 }, Select(600, 28, 1000, 84));
        Assert.Empty(Select(700, 28, 1000, 84));
    }

    [Fact]
    public void Column_resize_changes_the_selection_boundary()
    {
        Assert.Empty(Select(700, 0, 800, 28));
        Assert.Equal(new[] { 0 }, Select(700, 0, 800, 28, name: 400));
    }

    [Fact]
    public void Scrolled_rectangle_uses_content_coordinates()
    {
        const double horizontalOffset = 300;
        const double verticalOffset = 280;
        Assert.Equal(new[] { 10, 11 }, Select(
            10 + horizontalOffset, verticalOffset,
            40 + horizontalOffset, 56 + verticalOffset));
        Assert.Empty(Select(
            320 + horizontalOffset, verticalOffset,
            400 + horizontalOffset, 56 + verticalOffset));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(26, 30)]
    [InlineData(560, 600)]
    public void Row_gaps_and_space_below_last_row_remain_blank(double top, double bottom)
    {
        Assert.Empty(Select(20, top, 100, bottom));
    }

    private static List<int> Select(double left, double top, double right, double bottom, double name = 240)
    {
        List<int> hits = [];
        FileColumnLayout.CollectIndicesInRect(left, top, right, bottom, 20, name, 148, 100, 88, hits);
        return hits;
    }
}
