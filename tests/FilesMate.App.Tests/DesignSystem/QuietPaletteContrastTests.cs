using FilesMate.App.Models;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class QuietPaletteContrastTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Small_type_labels_and_body_text_have_readable_contrast(string theme)
    {
        var entries = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"))[theme]
            .Elements().ToDictionary(e => (string)e.Attribute(ThemeXaml.Xaml + "Key")!);
        uint Color(string key) => Convert.ToUInt32(((string)entries[key].Attribute("Color")!)[1..], 16);
        foreach (var tone in Enum.GetNames<FileTypeTone>())
            Assert.True(Contrast(Color($"FilesMate.Type.{tone}.ForegroundBrush"), Color($"FilesMate.Type.{tone}.BackgroundBrush")) >= 4.5,
                $"{theme} {tone} label is not legible.");
        foreach (var surface in new[] { "FileContent.BackgroundBrush", "Sidebar.BackgroundBrush", "FileArea.BackgroundBrush" })
        foreach (var ink in new[] { "Text.PrimaryBrush", "Text.SecondaryBrush" })
            Assert.True(Contrast(Color("FilesMate." + ink), Color("FilesMate." + surface)) >= 4.5, $"{theme} {ink} on {surface}");
    }

    [Fact]
    public void Primary_buttons_choose_legible_ink_for_both_themes_and_custom_accents()
    {
        foreach (var dark in new[] { false, true })
        foreach (var color in AccentPalette.Presets.Select(p => AccentPalette.Resolve(p.Kind, null, dark))
            .Concat(new uint[] { 0xFFFFFFFF, 0xFF000000, 0xFFFFFF00, 0xFF777777, 0xFF004466 }))
            Assert.True(Contrast(AccentPalette.Foreground(color), color) >= 4.5, $"Ink on {color:X8}");
    }

    [Fact]
    public void Companion_surface_and_xaml_fallback_share_the_same_palette()
    {
        foreach (var dark in new[] { false, true })
        {
            var theme = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"))[dark ? "Dark" : "Light"];
            var surface = theme.Elements().Single(e => (string?)e.Attribute(ThemeXaml.Xaml + "Key") == "FilesMate.SearchPanel.BackgroundBrush");
            Assert.Equal(AccentPalette.ToHex(SurfacePalette.Floating(dark)), (string?)surface.Attribute("Color"));
        }
    }

    [Fact]
    public void Decorative_tokens_have_system_color_fallbacks_in_high_contrast()
    {
        var theme = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"))["HighContrast"];
        var keys = theme.Elements().Where(e => ((string?)e.Attribute(ThemeXaml.Xaml + "Key")) is { } key
            && (key.StartsWith("FilesMate.Type.") || key.StartsWith("FilesMate.Place.")));
        Assert.NotEmpty(keys);
        Assert.All(keys, e => Assert.Contains("{ThemeResource SystemColor", (string?)e.Attribute("Color")));
        Assert.Equal("{ThemeResource SystemColorWindowColor}", (string?)theme.Elements().Single(e => (string?)e.Attribute(ThemeXaml.Xaml + "Key") == "FilesMate.Item.StripeBrush").Attribute("Color"));
    }

    private static double Contrast(uint first, uint second)
    {
        static double Luminance(uint color)
        {
            static double Linear(uint component) => component / 255d <= 0.04045
                ? component / 255d / 12.92 : Math.Pow((component / 255d + .055) / 1.055, 2.4);
            return .2126 * Linear((color >> 16) & 255) + .7152 * Linear((color >> 8) & 255) + .0722 * Linear(color & 255);
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
