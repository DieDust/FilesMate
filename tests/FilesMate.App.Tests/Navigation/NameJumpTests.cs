using FilesMate.App.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class NameJumpTests
{
    [Fact]
    public void Repeated_letters_cycle_and_further_typing_refines_current_match()
    {
        var session = new NameJumpSession();
        string[] names = ["alpha", "archive", "beta", "bravo", "brush"];
        int Find(char c, int ms, int current) => session.Find(c, TimeSpan.FromMilliseconds(ms), current, names.Length, i => names[i]);
        Assert.Equal(0, Find('a', 0, -1));
        Assert.Equal(1, Find('A', 100, 0));
        Assert.Equal(0, Find('a', 200, 1));
        Assert.Equal(2, Find('b', 1300, 0));
        Assert.Equal(3, Find('r', 1400, 2));
        Assert.Equal(4, Find('u', 1500, 3));
        session.Reset();
        Assert.Equal(0, Find('a', 1600, 4));
        Assert.Equal(-1, Find('\r', 1700, 0));
    }

    [Fact]
    public void Search_field_remains_visible_when_space_allows_and_fits_compact_windows()
    {
        Assert.Equal(36, OmnibarSearchLayout.Width(400, active: false));
        Assert.Equal(160, OmnibarSearchLayout.Width(400, active: true));
        Assert.Equal(180, OmnibarSearchLayout.Width(600, active: false));
        Assert.Equal(280, OmnibarSearchLayout.Width(1400, active: false));
        Assert.Equal(OmnibarSearchLayout.Width(800, false), OmnibarSearchLayout.Width(800, true));
    }
}
