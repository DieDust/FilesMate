using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class ThemeResourceContractTests
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Scrollbar_indicators_remain_visible_on_sidebar_and_expanded_track(string theme)
    {
        var entries = ThemeXaml.ThemeDictionaries(ThemeXaml.Load("Themes/AppThemeResources.xaml"))[theme]
            .Elements().Where(e => e.Attribute(ThemeXaml.Xaml + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(ThemeXaml.Xaml + "Key")!, StringComparer.Ordinal);
        double Luminance(string key)
        {
            var color = (string)entries[key].Attribute("Color")!;
            Assert.StartsWith("#FF", color, StringComparison.Ordinal);
            double Channel(int start)
            {
                var value = Convert.ToInt32(color.Substring(start, 2), 16) / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return Channel(3) * 0.2126 + Channel(5) * 0.7152 + Channel(7) * 0.0722;
        }
        foreach (var thumb in new[] { "ScrollBarPanningThumbBackground", "ScrollBarThumbBackground", "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed", "ScrollBarButtonArrowForeground" })
        foreach (var background in new[] { "FilesMate.Sidebar.BackgroundBrush", "ScrollBarTrackFillPointerOver" })
        {
            var a = Luminance(thumb);
            var b = Luminance(background);
            Assert.True((Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05) >= 3, $"{theme}: {thumb} lacks contrast against {background}.");
        }
    }

    [Fact]
    public void App_xaml_merges_theme_dictionaries_in_stable_order()
    {
        var appXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "App.xaml"));
        Assert.Contains("XamlControlsResources", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"QuietButtonStyle\"", appXaml, StringComparison.Ordinal);

        var sources = Regex.Matches(appXaml, @"Source=""ms-appx:///([^""]+)""")
            .Select(match => match.Groups[1].Value.Replace('\\', '/'))
            .ToArray();
        Assert.Equal(ThemeXaml.MergeOrder, sources);

        var controlsIndex = appXaml.IndexOf("XamlControlsResources", StringComparison.Ordinal);
        var firstTheme = appXaml.IndexOf("ms-appx:///Themes/DesignTokens.xaml", StringComparison.Ordinal);
        Assert.True(controlsIndex >= 0 && controlsIndex < firstTheme);
    }

    [Fact]
    public void Semantic_brushes_exist_in_light_dark_and_high_contrast()
    {
        var document = ThemeXaml.Load("Themes/AppThemeResources.xaml");
        var themes = ThemeXaml.ThemeDictionaries(document);
        foreach (var name in ThemeXaml.ThemeNames)
        {
            Assert.True(themes.ContainsKey(name), $"Missing ThemeDictionary '{name}'.");
            var keys = ThemeXaml.Keys(themes[name]);
            foreach (var expected in ThemeXaml.SemanticBrushKeys)
            {
                Assert.True(keys.Contains(expected), $"Theme '{name}' is missing '{expected}'.");
            }
        }
    }

    [Fact]
    public void Light_and_dark_shell_layers_remain_distinct_without_relying_on_the_wallpaper()
    {
        var document = ThemeXaml.Load("Themes/AppThemeResources.xaml");
        var themes = ThemeXaml.ThemeDictionaries(document);
        string[] keys =
        [
            "FilesMate.Chrome.FillBrush",
            "FilesMate.CommandBar.BackgroundBrush",
            "FilesMate.Sidebar.BackgroundBrush",
            "FilesMate.FileArea.BackgroundBrush",
            "FilesMate.FileContent.BackgroundBrush",
            "FilesMate.Favorites.BackgroundBrush",
        ];

        foreach (var name in new[] { "Light", "Dark" })
        {
            var map = themes[name]
                .Elements()
                .Where(element => element.Attribute(ThemeXaml.Xaml + "Key") is not null)
                .ToDictionary(
                    element => (string)element.Attribute(ThemeXaml.Xaml + "Key")!,
                    element => element,
                    StringComparer.Ordinal);
            foreach (var key in keys)
            {
                Assert.True(map.ContainsKey(key), $"Theme '{name}' is missing '{key}'.");
                Assert.StartsWith("#FF", (string?)map[key].Attribute("Color"));
            }
            var layers = new[] { "FilesMate.Chrome.FillBrush", "FilesMate.Sidebar.BackgroundBrush", "FilesMate.FileContent.BackgroundBrush" }
                .Select(key => (string?)map[key].Attribute("Color")).ToArray();
            Assert.Equal(layers.Length, layers.Distinct().Count());
        }
    }

    [Fact]
    public void Light_ink_stays_dark_and_theme_previews_stay_distinct()
    {
        var document = ThemeXaml.Load("Themes/AppThemeResources.xaml");
        var themes = ThemeXaml.ThemeDictionaries(document);
        var light = themes["Light"]
            .Elements()
            .Where(element => element.Attribute(ThemeXaml.Xaml + "Key") is not null)
            .ToDictionary(
                element => (string)element.Attribute(ThemeXaml.Xaml + "Key")!,
                element => element,
                StringComparer.Ordinal);
        var dark = themes["Dark"]
            .Elements()
            .Where(element => element.Attribute(ThemeXaml.Xaml + "Key") is not null)
            .ToDictionary(
                element => (string)element.Attribute(ThemeXaml.Xaml + "Key")!,
                element => element,
                StringComparer.Ordinal);

        Assert.Equal("SolidColorBrush", light["FilesMate.Text.PrimaryBrush"].Name.LocalName);
        Assert.Equal("#E4000000", (string?)light["FilesMate.Text.PrimaryBrush"].Attribute("Color"));
        Assert.Equal("#B3000000", (string?)light["FilesMate.Text.SecondaryBrush"].Attribute("Color"));
        Assert.Equal("#E4FFFFFF", (string?)dark["FilesMate.Text.PrimaryBrush"].Attribute("Color"));
        Assert.Equal("#B8FFFFFF", (string?)dark["FilesMate.Text.SecondaryBrush"].Attribute("Color"));
        Assert.Equal("#FFF3F3F3", (string?)light["FilesMate.ThemePreview.LightFill"].Attribute("Color"));
        Assert.Equal("#FF1C1C1E", (string?)light["FilesMate.ThemePreview.DarkFill"].Attribute("Color"));
        Assert.Equal(
            (string?)light["FilesMate.ThemePreview.LightFill"].Attribute("Color"),
            (string?)dark["FilesMate.ThemePreview.LightFill"].Attribute("Color"));
        Assert.Equal(
            (string?)light["FilesMate.ThemePreview.DarkFill"].Attribute("Color"),
            (string?)dark["FilesMate.ThemePreview.DarkFill"].Attribute("Color"));
        Assert.NotEqual(
            (string?)light["FilesMate.ThemePreview.LightFill"].Attribute("Color"),
            (string?)light["FilesMate.ThemePreview.DarkFill"].Attribute("Color"));
        Assert.Equal("#C4FFFFFF", (string?)light["FilesMate.AddressBar.BackgroundBrush"].Attribute("Color"));
        Assert.NotEqual((string?)light["FilesMate.Tab.BackgroundBrush"].Attribute("Color"),
            (string?)light["FilesMate.Tab.SelectedBrush"].Attribute("Color"));
        Assert.Equal("#FFFFFFFF", (string?)light["FilesMate.Tab.SelectedBrush"].Attribute("Color"));
        Assert.Equal("#FFF2F2F7", (string?)light["ComboBoxDropDownBackground"].Attribute("Color"));
        Assert.Equal("#E4000000", (string?)light["ComboBoxItemForegroundSelected"].Attribute("Color"));
        Assert.Equal("#E4000000", (string?)light["ComboBoxItemForegroundSelectedUnfocused"].Attribute("Color"));
        Assert.Equal("#E4000000", (string?)light["TextControlForegroundFocused"].Attribute("Color"));
        Assert.Equal("#E4000000", (string?)light["TextControlForeground"].Attribute("Color"));
        Assert.Equal("#66000000", (string?)dark["FilesMate.SettingsPanel.BackgroundBrush"].Attribute("Color"));
        Assert.Equal("#FF252528", (string?)dark["FilesMate.SettingsCard.BackgroundBrush"].Attribute("Color"));
        Assert.Equal("#FF2C2C2E", (string?)dark["FilesMate.ComboBox.BackgroundBrush"].Attribute("Color"));
        Assert.Equal("#FFF2F2F7", (string?)light["FilesMate.ComboBox.BackgroundBrush"].Attribute("Color"));
        Assert.Equal("#3D000000", (string?)light["FilesMate.ComboBox.BorderBrush"].Attribute("Color"));
        Assert.DoesNotContain(
            "LayerOnMicaBaseAltFillColorDefault",
            (string?)light["FilesMate.AddressBar.BackgroundBrush"].Attribute("Color") +
            (string?)light["FilesMate.Tab.SelectedBrush"].Attribute("Color"),
            StringComparison.Ordinal);
        Assert.Equal(
            "FilesMate.Text.PrimaryBrush",
            (string?)light["TabViewItemHeaderForeground"].Attribute("ResourceKey"));
        Assert.Equal(
            "FilesMate.Text.PrimaryBrush",
            (string?)light["TabViewItemHeaderForegroundSelected"].Attribute("ResourceKey"));
    }

    [Fact]
    public void High_contrast_brushes_alias_system_colors_instead_of_transparency()
    {
        var document = ThemeXaml.Load("Themes/AppThemeResources.xaml");
        var highContrast = ThemeXaml.ThemeDictionaries(document)["HighContrast"];
        foreach (var entry in highContrast.Elements())
        {
            var key = (string?)entry.Attribute(ThemeXaml.Xaml + "Key");
            if (key is null || !key.StartsWith("FilesMate.", StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.Name.LocalName == "StaticResource")
            {
                var resourceKey = (string?)entry.Attribute("ResourceKey");
                Assert.False(string.IsNullOrEmpty(resourceKey), $"{key} is missing ResourceKey.");
                Assert.StartsWith("SystemColor", resourceKey, StringComparison.Ordinal);
                continue;
            }

            var color = (string?)entry.Attribute("Color")
                ?? (string?)entry.Elements().FirstOrDefault()?.Attribute("Color");
            Assert.False(string.IsNullOrEmpty(color), $"{key} has no Color or StaticResource alias.");
            Assert.DoesNotContain("Transparent", color, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Design_tokens_define_spacing_radius_and_control_metrics()
    {
        var document = ThemeXaml.Load("Themes/DesignTokens.xaml");
        var keys = ThemeXaml.Keys(document.Root!);
        string[] required =
        [
            "FilesMate.Space.1",
            "FilesMate.Space.2",
            "FilesMate.Space.3",
            "FilesMate.Space.4",
            "FilesMate.Space.5",
            "FilesMate.Space.6",
            "FilesMate.Space.8",
            "FilesMate.Corner.Small",
            "FilesMate.Corner.Omnibar",
            "FilesMate.Corner.Card",
            "FilesMate.Corner.Illustration",
            "FilesMate.Corner.Pill",
            "FilesMate.Control.Height.Row",
            "FilesMate.Control.Height.ContextItem",
            "FilesMate.Control.Height.SidebarItem",
            "FilesMate.Control.Height.Toolbar",
            "FilesMate.Control.Height.Omnibar",
            "FilesMate.Control.Height.StatusBar",
            "FilesMate.Control.Height.TitleBar",
            "FilesMate.Tab.Height",
            "FilesMate.Tab.MinWidth",
            "FilesMate.Tab.MaxWidth",
            "FilesMate.Tab.DragMinWidth",
            "FilesMate.Sidebar.Width",
            "FilesMate.Sidebar.Width.Medium",
            "FilesMate.Sidebar.Width.Compact",
            "FilesMate.Icon.Size.Small",
            "FilesMate.Icon.Size.Medium",
            "FilesMate.Icon.Size.Large",
            "FilesMate.ContextMenu.MinWidth",
            "FilesMate.ContextMenu.MaxWidth",
            "FilesMate.Pane.Gutter",
            "FilesMate.Column.Accent",
            "FilesMate.Column.Glyph",
            "FilesMate.Column.Name",
            "FilesMate.Column.Modified",
            "FilesMate.Column.Type",
            "FilesMate.Column.Size",
            "FilesMate.Item.HiddenOpacity",
            "FilesMate.Motion.Instant",
            "FilesMate.Motion.Fast",
            "FilesMate.Motion.Standard",
            "FilesMate.Motion.Emphasized",
        ];

        foreach (var key in required)
        {
            Assert.True(keys.Contains(key), $"DesignTokens.xaml is missing '{key}'.");
        }
    }

    [Fact]
    public void Migrated_button_styles_live_in_the_theme_dictionaries()
    {
        var buttonKeys = ThemeXaml.Keys(ThemeXaml.Load("Themes/ButtonStyles.xaml").Root!);
        var navigationKeys = ThemeXaml.Keys(ThemeXaml.Load("Themes/NavigationStyles.xaml").Root!);
        var surfaceKeys = ThemeXaml.Keys(ThemeXaml.Load("Themes/FileSurfaceStyles.xaml").Root!);

        Assert.Contains("QuietButtonStyle", buttonKeys);
        Assert.Contains("ToolbarIconButtonStyle", buttonKeys);
        Assert.Contains("CommandBarButtonStyle", buttonKeys);
        Assert.Contains("GlassButtonStyle", buttonKeys);
        Assert.Contains("PillButtonStyle", buttonKeys);
        Assert.Contains("SidebarItemStyle", navigationKeys);
        Assert.Contains("ColumnHeaderButtonStyle", surfaceKeys);

        var buttonsXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "ButtonStyles.xaml"));
        Assert.Contains("FilesMate.Corner.Pill", buttonsXaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Glass.SurfaceBrush", buttonsXaml, StringComparison.Ordinal);
        Assert.Contains("FilesMate.Glass.AccentBrush", buttonsXaml, StringComparison.Ordinal);
        var themeXaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "Themes", "AppThemeResources.xaml"));
        Assert.Contains("FilesMate.Glass.SheenBrush", themeXaml, StringComparison.Ordinal);

        var all = buttonKeys
            .Concat(navigationKeys)
            .Concat(surfaceKeys)
            .Concat(ThemeXaml.Keys(ThemeXaml.Load("Themes/MenuStyles.xaml").Root!))
            .ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Merged_theme_files_do_not_repeat_style_or_token_keys()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in ThemeXaml.MergeOrder)
        {
            var document = ThemeXaml.Load(relative);
            foreach (var key in ThemeXaml.Keys(document.Root!))
            {
                Assert.True(seen.Add(key), $"Resource key '{key}' is defined more than once across theme dictionaries.");
            }
        }
    }
}
