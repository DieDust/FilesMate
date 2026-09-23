using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Entries;

public sealed class AlphabetNavigationTests
{
    [Fact]
    public void Fixed_alphabet_uses_closest_run_without_cycling()
    {
        var navigation = new AlphabetNavigation([new("A", 0, true), new("C", 5, true), new("A", 80, false), new("C", 100, false)], 160);
        Assert.Equal(28, AlphabetNavigation.Labels.Count);
        Assert.Equal(28, AlphabetNavigation.Labels.Distinct().Count());
        Assert.Equal(1, navigation.Destination("C", 2));
        Assert.Equal(3, navigation.Destination("C", 95));
        Assert.Equal(1, navigation.Destination("C", 70));
        Assert.Equal(3, navigation.Destination("C", 120));
        Assert.True(navigation.HasBothKinds("C"));
        Assert.Equal(1, navigation.Destination("C", 120, isDirectory: true));
        Assert.Equal(3, navigation.Destination("C", 2, isDirectory: false));
        Assert.Equal(2, navigation.Occurrences("A"));
        Assert.Equal([0, 2], navigation.Destinations("A"));
        Assert.Empty(navigation.Destinations("Z"));
        Assert.Equal(0, navigation.Occurrences("Z"));
        Assert.Equal(0, navigation.BoundaryDestination("A", last: false));
        Assert.Equal(2, navigation.BoundaryDestination("A", last: true));
        Assert.Equal(-1, navigation.Destination("Z", 95));
        Assert.Equal(-1, navigation.Destination("Z", 95, isDirectory: true));
    }

    [Fact]
    public void Same_letter_in_two_folder_runs_does_not_need_a_choice()
    {
        var navigation = new AlphabetNavigation([new("F", 0, true), new("G", 10, true),
            new("F", 20, true), new("Z", 30, false)], 40);

        Assert.Equal(2, navigation.Occurrences("F"));
        Assert.False(navigation.HasBothKinds("F"));
        Assert.Equal(0, navigation.Destination("F", 3));
        Assert.Equal(2, navigation.Destination("F", 19));
        Assert.Equal(2, navigation.Destination("F", 25));
        Assert.Equal(-1, navigation.Destination("F", 25, isDirectory: false));
    }

    [Fact]
    public void Highlight_uses_actual_top_row_instead_of_percentage_of_all_files()
    {
        var navigation = new AlphabetNavigation([new("A", 0, true), new("M", 35, true), new("S", 46, true)], 75);
        var top = navigation.FirstVisibleIndex(35 * 32, 32);
        Assert.Equal(35, top);
        Assert.Equal("M", navigation.Sections[navigation.SectionAt(top)].Label);
        Assert.Equal(46 * 32, navigation.OffsetForItem(46, 32));
        Assert.Equal("S", navigation.Sections[navigation.SectionAt(navigation.FirstVisibleIndex(46 * 32, 32))].Label);
    }

    [Fact]
    public void Final_letter_can_really_align_at_top_with_viewport_tail_space()
    {
        var navigation = new AlphabetNavigation([new("A", 0, false), new("I", 180, false), new("Z", 205, false)], 206);
        const double stride = 32, rowHeight = 30, viewport = 640;
        var contentHeight = (navigation.Count - 1) * stride + rowHeight;
        var tail = viewport - rowHeight;
        var maximumOffset = contentHeight + tail - viewport;
        var requested = navigation.OffsetForItem(205, stride);
        Assert.Equal(maximumOffset, requested);
        Assert.Equal(205, navigation.FirstVisibleIndex(requested, stride));
        Assert.Equal("Z", navigation.Sections[navigation.SectionAt(navigation.FirstVisibleIndex(requested, stride))].Label);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(159, 0)]
    [InlineData(160, 4)]
    [InlineData(320, 8)]
    public void Grid_uses_the_first_item_of_the_actual_visible_row(double offset, int expected)
    {
        var navigation = new AlphabetNavigation([new("A", 0, false)], 20);
        Assert.Equal(expected, navigation.FirstVisibleIndex(offset, 160, 4));
        Assert.Equal(320, navigation.OffsetForItem(11, 160, 4));
    }

    [Fact]
    public void Ranges_follow_rows_and_grow_with_the_number_of_files()
    {
        var navigation = new AlphabetNavigation([new("A", 0, false), new("C", 2, false), new("Z", 102, false)], 105);
        Assert.Equal((0, 2), navigation.Range(0));
        Assert.Equal((2, 102), navigation.Range(1));
        Assert.Equal((102, 105), navigation.Range(2));
        Assert.Equal(0, navigation.SectionAt(1));
        Assert.Equal(1, navigation.SectionAt(2));
        Assert.Equal(1, navigation.SectionAt(101));
        Assert.Equal(2, navigation.SectionAt(104));
        Assert.Equal(-1, new AlphabetNavigation([], 0).SectionAt(0));
    }

    [Theory]
    [InlineData("阿.txt", "A")]
    [InlineData("C10.txt", "C")]
    [InlineData("_cache", "#")]
    [InlineData("123.txt", "#")]
    [InlineData("Ω.txt", "其他")]
    [InlineData("😀.txt", "其他")]
    public void Initial_labels_do_not_expose_language_prefixes(string name, string expected)
        => Assert.Equal(expected, PinyinName.Initial(PinyinName.Key(name)));
}
