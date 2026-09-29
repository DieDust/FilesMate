using FilesMate.App.Models;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class SkinPaletteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generated_resources_match_the_shared_palette_and_have_no_unassigned_skin_colors(bool dark)
    {
        var document = ThemeXaml.Load("Themes/AppThemeResources.xaml");
        var theme = ThemeXaml.ThemeDictionaries(document)[dark ? "Dark" : "Light"];
        var colors = SkinPalette.For(dark).ResourceColors();
        foreach (var (key, color) in colors)
        {
            var element = ThemeXaml.Resolve(theme, key);
            var value = element.Name.LocalName == "Color" ? element.Value : (string?)(element.Attribute("Color") ?? element.Attribute("FallbackColor"));
            Assert.True(value == AccentPalette.ToHex(color), $"{key}: regenerate theme resources from SkinPalette.cs");
            if (element.Attribute("TintColor") is { } tint) Assert.Equal(AccentPalette.ToHex(color), tint.Value);
        }
        foreach (var element in theme.Elements())
        {
            var key = (string?)element.Attribute(ThemeXaml.Xaml + "Key");
            if (key is null || Functional(key) || element.Name.LocalName == "StaticResource" || element.Name.LocalName == "LinearGradientBrush") continue;
            var value = (string?)(element.Attribute("Color") ?? element.Attribute("FallbackColor")) ?? element.Value;
            if (value == "Transparent" || value.StartsWith("{ThemeResource", StringComparison.Ordinal)) continue;
            Assert.True(colors.ContainsKey(key), $"{key}: skin colors must have a shared palette role");
        }
    }

    [Fact]
    public void Companion_startup_fallbacks_share_the_same_skin_as_its_live_resources()
    {
        var document = ThemeXaml.Load("../FilesMate.SearchHost/PaletteWindow.xaml");
        foreach (var (key, color) in SkinPalette.DarkSkin.CompanionColors())
        {
            var element = document.Descendants().Single(e => (string?)e.Attribute(ThemeXaml.Xaml + "Key") == key);
            Assert.Equal(AccentPalette.ToHex(color), (string?)element.Attribute("Color"));
        }
    }

    [Fact]
    public void Replacing_a_skin_preserves_component_roles_and_updates_surface_and_text_together()
    {
        var custom = SkinPalette.Light with
        {
            Canvas = 0xFFECEFF4, Navigation = 0xFFE2E7EE, Chrome = 0xFFDAE1EA,
            Content = 0xFFFDFEFF, Card = 0xFFF8FAFF, Floating = 0xFFF1F4FA, Input = 0xFFF5F7FC,
            Text = 0xFF192C48, Muted = 0xFF495C78, Line = 0xFF344C69, Accent = 0xFF2C5692,
        };
        var before = SkinPalette.Light.ResourceColors(); var after = custom.ResourceColors();
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        Assert.Equal(custom.Content, after["FilesMate.FileContent.BackgroundBrush"]);
        Assert.Equal(custom.Navigation, after["FilesMate.Sidebar.BackgroundBrush"]);
        Assert.Equal(custom.Card, after["ContentDialogTopOverlay"]);
        Assert.Equal(custom.Floating, after["ComboBoxDropDownBackground"]);
        Assert.Equal(custom.Text, after["FilesMate.Palette.Light.Text.PrimaryBrush"]);
        Assert.Equal(custom.Muted, after["FilesMate.Palette.Light.Text.SecondaryBrush"]);
        Assert.Equal(after["ButtonBackgroundPointerOver"], after["ComboBoxBackgroundPointerOver"]);
        Assert.Equal(after["FilesMate.Divider.Brush"], after["ContentDialogSeparatorBorderBrush"]);
        Assert.NotEqual(before["ButtonBackgroundPointerOver"], after["ButtonBackgroundPointerOver"]);
        Assert.DoesNotContain(after.Keys, Functional);
    }

    private static bool Functional(string key) => key.StartsWith("FilesMate.Compare.") && key != "FilesMate.Compare.SourceFillBrush"
        || key.StartsWith("FilesMate.Type.") || key.StartsWith("FilesMate.Place.") || key.StartsWith("FilesMate.Close.")
        || key.StartsWith("FilesMate.FolderPreview.") || key == "SystemFillColorCriticalBrush";
}
