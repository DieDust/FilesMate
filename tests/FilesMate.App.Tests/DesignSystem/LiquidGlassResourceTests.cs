using System.Xml.Linq;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class LiquidGlassResourceTests
{
    private static readonly string[] BrushKeys =
    [
        "FilesMate.LiquidGlass.SolidFillBrush",
        "FilesMate.LiquidGlass.FillBrush",
        "FilesMate.LiquidGlass.BorderBrush",
        "FilesMate.LiquidGlass.InnerHighlightBrush",
        "FilesMate.LiquidGlass.ShadowBrush",
    ];

    [Fact]
    public void Liquid_glass_brushes_have_light_dark_and_high_contrast_values()
    {
        var themes = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"));
        foreach (var themeName in ThemeXaml.ThemeNames)
        {
            var keys = ThemeXaml.Keys(themes[themeName]);
            foreach (var brushKey in BrushKeys)
            {
                Assert.Contains(brushKey, keys);
            }
        }
    }

    [Fact]
    public void Liquid_glass_tokens_do_not_define_a_moving_scene()
    {
        var tokens = ThemeXaml.Keys(ThemeXaml.Load("Themes/DesignTokens.xaml").Root!);
        string[] removed =
        [
            "FilesMate.Glass.Pointer.Opacity",
            "FilesMate.Glass.Pointer.Radius",
            "FilesMate.Glass.Ambient.Opacity",
            "FilesMate.Glass.Ambient.Width",
            "FilesMate.Glass.Ambient.DurationSeconds",
            "FilesMate.Glass.Parallax.MaxOffset",
        ];

        foreach (var key in removed)
        {
            Assert.DoesNotContain(key, tokens);
        }
    }

    [Fact]
    public void Reusable_surface_exposes_a_static_material_and_fallback()
    {
        var styles = ThemeXaml.Load("Themes/LiquidGlassStyles.xaml");
        var keys = ThemeXaml.Keys(styles.Root!);
        Assert.Contains("FilesMate.LiquidGlassSurfaceStyle", keys);

        var styleSource = File.ReadAllText(Path.Combine(
            ThemeXaml.ThemesRoot,
            "LiquidGlassStyles.xaml"));
        Assert.Contains("PART_SolidLayer", styleSource, StringComparison.Ordinal);
        Assert.Contains("PART_MaterialLayer", styleSource, StringComparison.Ordinal);
        Assert.Contains("PART_InnerHighlight", styleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PART_PointerLight", styleSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PART_AmbientSheen", styleSource, StringComparison.Ordinal);
        Assert.Contains("FilesMate.LiquidGlass.FillBrush", styleSource, StringComparison.Ordinal);

        var controlSource = File.ReadAllText(Path.Combine(
            ThemeXaml.AppRoot,
            "Controls",
            "Glass",
            "LiquidGlassSurface.cs"));
        Assert.Contains("SurfaceKindProperty", controlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsInteractiveLightingEnabledProperty", controlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAmbientSheenEnabledProperty", controlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsRepeater", controlSource, StringComparison.Ordinal);
    }
}
