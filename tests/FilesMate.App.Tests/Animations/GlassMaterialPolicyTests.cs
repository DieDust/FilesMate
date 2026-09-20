using FilesMate.App.Animations;

namespace FilesMate.App.Tests.Animations;

public sealed class GlassMaterialPolicyTests
{
    [Fact]
    public void Strong_glass_retains_neutral_color_and_small_changes_are_continuous()
    {
        foreach (var resolve in new Func<double, double>[] { GlassMaterialPolicy.FoundationCoverage, GlassMaterialPolicy.LayerCoverage,
            GlassMaterialPolicy.ContentCoverage, GlassMaterialPolicy.FloatingCoverage, GlassMaterialPolicy.CardCoverage })
        {
            Assert.Equal(1, resolve(1));
            Assert.InRange(resolve(0), .30, .60);
            for (var percent = 1; percent <= 100; percent++)
            {
                var previous = resolve(1 - (percent - 1) / 100d);
                var current = resolve(1 - percent / 100d);
                Assert.InRange(previous - current, 0, .012);
            }
            Assert.Equal(resolve(0), resolve(-1));
            Assert.Equal(resolve(1), resolve(2));
        }
    }

    [Fact]
    public void Nested_file_surfaces_leave_visible_background_at_high_strength()
    {
        double Transmission(double strength) => (1 - GlassMaterialPolicy.LayerCoverage(1 - strength)) *
            (1 - GlassMaterialPolicy.FoundationCoverage(1 - strength));
        Assert.Equal(0, Transmission(0));
        Assert.InRange(Transmission(.5), .15, .17);
        Assert.InRange(Transmission(.73), .28, .30);
        Assert.InRange(Transmission(1), .40, .43);
    }

    [Theory]
    [InlineData(37, 48, 41)]
    [InlineData(41, 53, 45)]
    [InlineData(47, 60, 51)]
    [InlineData(240, 255, 255)]
    [InlineData(243, 255, 255)]
    [InlineData(247, 255, 255)]
    public void Sidebar_content_contrast_survives_every_strength_and_background(byte sidebar, byte content, byte neutral)
    {
        for (var percent = 0; percent <= 100; percent++)
        {
            var coverage = GlassMaterialPolicy.LayerCoverage(1 - percent / 100d);
            var sidebarTint = GlassMaterialPolicy.ContrastTint(sidebar, neutral, coverage);
            var contentTint = GlassMaterialPolicy.ContrastTint(content, neutral, coverage);
            for (var background = 0; background <= 255; background += 15)
            {
                var renderedSidebar = coverage * sidebarTint + (1 - coverage) * background;
                var renderedContent = coverage * contentTint + (1 - coverage) * background;
                Assert.InRange(renderedContent - renderedSidebar, content - sidebar - 1d, content - sidebar + 1d);
            }
        }
    }

    [Fact]
    public void Floating_and_nested_shell_have_the_same_background_transmission()
    {
        for (var percent = 0; percent <= 100; percent++)
        {
            var opacity = 1 - percent / 100d;
            var shell = (1 - GlassMaterialPolicy.FoundationCoverage(opacity)) * (1 - GlassMaterialPolicy.LayerCoverage(opacity));
            Assert.Equal(shell, 1 - GlassMaterialPolicy.FloatingCoverage(opacity), 12);
        }
    }
}
