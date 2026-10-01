using FilesMate.App.Models;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class FileInteractionPaletteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selection_outline_and_text_remain_legible_for_presets_and_extreme_custom_accents(bool dark)
    {
        foreach (var accent in AccentPalette.Presets.Select(preset => AccentPalette.Resolve(preset.Kind, null, dark))
            .Concat(new uint[] { 0xFFFFFFFF, 0xFF000000, 0xFFFFFF00, 0xFF777777, 0x004F786C }))
        {
            var skin = SkinPalette.For(dark) with { Accent = accent };
            var colors = skin.FileItemColors();
            foreach (var key in new[] { "SelectedBrush", "SelectedHoverBrush" })
            {
                var fill = Composite(colors["FilesMate.FileItem." + key], skin.Content);
                Assert.True(Contrast(colors["FilesMate.FileItem.SelectionBorderBrush"], fill) >= 3,
                    $"{dark}/{accent:X8}/{key}: selection outline merges with the fill.");
                Assert.True(Contrast(colors["FilesMate.FileItem.SelectedForegroundBrush"], fill) >= 4.5,
                    $"{dark}/{accent:X8}/{key}: selected name/details are not legible.");
            }
            Assert.Equal(0xFFu, colors["FilesMate.FileItem.SelectionBorderBrush"] >> 24);
            Assert.True(Contrast(colors["FilesMate.FileItem.CheckForegroundBrush"], colors["FilesMate.FileItem.SelectionBorderBrush"]) >= 4.5,
                $"{dark}/{accent:X8}: checkbox glyph is not legible.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hover_is_distinct_from_stripes_keeps_secondary_text_readable_and_does_not_follow_accent(bool dark)
    {
        var skin = SkinPalette.For(dark);
        var colors = skin.FileItemColors();
        var hover = Composite(colors["FilesMate.FileItem.HoverBrush"], skin.Content);
        var stripe = Composite(skin.ResourceColors()["FilesMate.Item.StripeBrush"], skin.Content);
        Assert.True(Contrast(skin.Muted, hover) >= 4.5);
        Assert.True(Contrast(Composite(colors["FilesMate.FileItem.HoverBorderBrush"], hover), hover) >= 1.3);
        Assert.True(Contrast(hover, stripe) > 1.08, "Hover disappears into alternating rows.");
        var changedAccent = (skin with { Accent = 0xFFE81123 }).FileItemColors();
        Assert.Equal(colors["FilesMate.FileItem.HoverBrush"], changedAccent["FilesMate.FileItem.HoverBrush"]);
        Assert.Equal(colors["FilesMate.FileItem.HoverBorderBrush"], changedAccent["FilesMate.FileItem.HoverBorderBrush"]);
        Assert.NotEqual(colors["FilesMate.FileItem.SelectionBorderBrush"], changedAccent["FilesMate.FileItem.SelectionBorderBrush"]);
    }

    [Fact]
    public void High_contrast_uses_highlight_ink_for_selection_and_hotlight_outline_for_hover()
    {
        var theme = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"))["HighContrast"];
        Assert.Equal("SystemColorHighlightTextColorBrush", (string?)ThemeXaml.Resolve(theme, "FilesMate.FileItem.SelectedForegroundBrush").Attribute("ResourceKey"));
        Assert.Equal("SystemColorHotlightColorBrush", (string?)ThemeXaml.Resolve(theme, "FilesMate.FileItem.HoverBorderBrush").Attribute("ResourceKey"));
    }

    private static uint Composite(uint color, uint background) => SkinPalette.Mix(color, background, (color >> 24) / 255d);

    private static double Contrast(uint first, uint second)
    {
        static double Luminance(uint color)
        {
            static double Linear(uint channel)
            {
                var value = channel / 255d;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Linear((color >> 16) & 255) + .7152 * Linear((color >> 8) & 255) + .0722 * Linear(color & 255);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
