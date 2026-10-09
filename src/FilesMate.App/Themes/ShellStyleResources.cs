using FilesMate.App.Models;
using FilesMate.App.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FilesMate.App.Themes;

internal static class ShellStyleResources
{
    internal static bool OwnsPalette(ResourceDictionary resources) => string.Equals(
        resources.Source?.OriginalString, "ms-appx:///Themes/AppThemeResources.xaml", StringComparison.OrdinalIgnoreCase);

    public static void Apply(ResourceDictionary resources, AppearanceSettings settings)
    {
        // Contrast-theme aliases can refer to immutable Windows brushes. Never
        // recolor them, or let a custom low-contrast palette replace their roles.
        if (Theming.GlassEnvironment.HighContrast) return;
        if (OwnsPalette(resources)) ApplyOwnedPalette(resources, settings);
        foreach (var merged in resources.MergedDictionaries) Apply(merged, settings);
    }

    private static void ApplyOwnedPalette(ResourceDictionary resources, AppearanceSettings settings)
    {
        var glass = GlassSceneState.Resolve(settings);
        var opacity = GlassMaterialPolicy.EffectiveOpacity(glass.SurfaceOpacity,
            Theming.GlassEnvironment.TransparencyEnabled, false);
        var surfaceOpacity = GlassMaterialPolicy.LayerCoverage(opacity);
        foreach (var name in new[] { "Light", "Dark" })
        {
            if (resources.ThemeDictionaries[name] is not ResourceDictionary theme) continue;
            var dark = name == "Dark";
            var palette = SkinPalette.For(dark, settings);
            var colors = palette.ResourceColors();
            // Start every update from the newly resolved semantic palette. Using
            // a brush's previous compensated tint would accumulate glass changes
            // or restore the first preset after switching to another skin.
            foreach (var (key, color) in colors)
            {
                var owner = key.StartsWith("FilesMate.Palette.", StringComparison.Ordinal) ? resources : theme;
                Paint(owner, key, FromArgb(color));
            }
            if (theme.TryGetValue("FilesMate.LiquidGlass.TopSheenBrush", out var sheen)
                && sheen is LinearGradientBrush gradient)
                foreach (var stop in gradient.GradientStops)
                    stop.Color = FromArgb(SkinPalette.Alpha(dark ? palette.Text : palette.Card, stop.Color.A));
            ApplyFloatingSurfaces(theme, palette, opacity);
            var neutral = FromArgb(dark ? palette.Canvas : palette.Card);
            Set(theme, colors, "FilesMate.Chrome.FillBrush", GlassMaterialPolicy.ConnectedCoverage(opacity), neutral);
            Set(theme, colors, "FilesMate.Sidebar.BackgroundBrush", surfaceOpacity, neutral);
            Set(theme, colors, "FilesMate.CommandBar.BackgroundBrush", surfaceOpacity, neutral);
            Set(theme, colors, "FilesMate.FileArea.BackgroundBrush", GlassMaterialPolicy.FoundationCoverage(opacity));
            Set(theme, colors, "FilesMate.Favorites.BackgroundBrush", surfaceOpacity, neutral);
            Set(theme, colors, "FilesMate.Shell.SeparatorBrush");
            Set(theme, colors, "FilesMate.FileContent.BorderBrush");
            Set(theme, colors, "FilesMate.FileContent.BackgroundBrush", GlassMaterialPolicy.ContentCoverage(opacity), neutral);
            var prefix = "FilesMate.Palette." + name + ".";
            Set(resources, colors, prefix + "Tab.BackgroundBrush");
            Set(resources, colors, prefix + "Tab.SelectedBrush", GlassMaterialPolicy.CardCoverage(opacity), neutral);
        }
    }

    private static void ApplyFloatingSurfaces(ResourceDictionary theme, SkinPalette palette, double surfaceOpacity)
    {
        var dark = palette.Dark;
        var color = FromArgb(palette.Floating);
        var coverage = GlassMaterialPolicy.FloatingCoverage(surfaceOpacity);
        foreach (var key in new[] { "FilesMate.Menu.BackgroundBrush", "FilesMate.LiquidGlass.FillBrush",
            "FilesMate.InfoPane.BackgroundBrush", "FilesMate.App.BackgroundBrush",
            "ContentDialogBackground", "ComboBoxDropDownBackground", "FilesMate.SearchPanel.BackgroundBrush" })
        {
            if (!theme.TryGetValue(key, out var value) || value is not AcrylicBrush brush) continue;
            brush.TintColor = color;
            brush.FallbackColor = color;
            brush.TintOpacity = coverage;
            brush.TintLuminosityOpacity = dark ? 0.55 : 0.65;
            brush.AlwaysUseFallback = surfaceOpacity == 1;
        }
        foreach (var key in new[] { "FilesMate.LiquidGlass.SolidFillBrush", "FilesMate.Update.SurfaceBrush" })
            Paint(theme, key, color);
        Paint(theme, "FilesMate.SettingsPanel.BackgroundBrush", color, coverage);
        var card = FromArgb(palette.Card);
        var cardCoverage = GlassMaterialPolicy.CardCoverage(surfaceOpacity);
        Paint(theme, "FilesMate.SettingsCard.BackgroundBrush", ContrastTint(card, dark ? color : card, cardCoverage), cardCoverage);
        Paint(theme, "ContentDialogTopOverlay", card);
        Paint(theme, "FilesMate.SettingsOverlay.ScrimBrush", Color.FromArgb(dark ? (byte)0x26 : (byte)0x1A, 0, 0, 0));
        var nav = FromArgb(palette.Navigation);
        Paint(theme, "FilesMate.SettingsNav.BackgroundBrush", ContrastTint(nav, dark ? color : card, cardCoverage), cardCoverage);
    }

    private static void Paint(ResourceDictionary owner, string key, Color color, double opacity = 1)
    {
        if (!owner.TryGetValue(key, out var value)) return;
        if (value is SolidColorBrush brush) { brush.Color = color; brush.Opacity = opacity; }
        else if (value is AcrylicBrush acrylic) { acrylic.TintColor = color; acrylic.FallbackColor = color; }
        else if (value is Color) owner[key] = color;
    }

    internal static Color FromArgb(uint value) => Color.FromArgb((byte)(value >> 24),
        (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private static Color ContrastTint(Color color, Color neutral, double coverage) => Color.FromArgb(color.A,
        GlassMaterialPolicy.ContrastTint(color.R, neutral.R, coverage),
        GlassMaterialPolicy.ContrastTint(color.G, neutral.G, coverage),
        GlassMaterialPolicy.ContrastTint(color.B, neutral.B, coverage));

    private static void Set(ResourceDictionary owner, IReadOnlyDictionary<string, uint> colors,
        string key, double opacity = 1, Color? neutral = null)
    {
        if (!colors.TryGetValue(key, out var source)) return;
        var color = FromArgb(source);
        Paint(owner, key, neutral is { } anchor ? ContrastTint(color, anchor, opacity) : color, opacity);
    }
}
