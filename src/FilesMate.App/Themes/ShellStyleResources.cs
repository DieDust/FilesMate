using FilesMate.App.Models;
using FilesMate.App.Animations;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FilesMate.App.Themes;

internal static class ShellStyleResources
{
    // Keep the declared layered palette so repeated switches never accumulate changes.
    // Mutating the existing brushes also updates loaded tabs without recreating their views.
    private static readonly Dictionary<SolidColorBrush, Color> LayeredColors = new();

    public static void Apply(ResourceDictionary resources, AppearanceSettings settings)
    {
        var style = settings.ShellStyle;
        var glass = GlassSceneState.Resolve(settings);
        // The slider controls glass strength, not the alpha of an entire region.
        // Retain a neutral veil even at maximum strength so wallpaper cannot erase layers.
        var surfaceOpacity = GlassMaterialPolicy.LayerCoverage(glass.SurfaceOpacity);
        foreach (var key in resources.ThemeDictionaries.Keys)
        {
            if (key is not string name || name == "HighContrast"
                || resources.ThemeDictionaries[key] is not ResourceDictionary theme) continue;
            var dark = name != "Light";
            ApplyFloatingSurfaces(theme, dark, glass.SurfaceOpacity);
            var neutral = dark ? Color.FromArgb(255, 41, 45, 51) : Colors.White;
            Set(theme, "FilesMate.Chrome.FillBrush", Colors.Transparent, style,
                GlassMaterialPolicy.FloatingCoverage(glass.SurfaceOpacity), neutral);
            Set(theme, "FilesMate.Sidebar.BackgroundBrush", Colors.Transparent, style, surfaceOpacity, neutral);
            Set(theme, "FilesMate.CommandBar.BackgroundBrush", Colors.Transparent, style, surfaceOpacity, neutral);
            Set(theme, "FilesMate.FileArea.BackgroundBrush", Colors.Transparent, style,
                GlassMaterialPolicy.FoundationCoverage(glass.SurfaceOpacity));
            Set(theme, "FilesMate.Favorites.BackgroundBrush", Colors.Transparent, style, surfaceOpacity, neutral);
            Set(theme, "FilesMate.Shell.SeparatorBrush", Colors.Transparent, style);
            Set(theme, "FilesMate.FileContent.BorderBrush", Colors.Transparent, style);
            if (theme.TryGetValue("FilesMate.Glass.CardBrush", out var card) && card is SolidColorBrush cardBrush)
                Set(theme, "FilesMate.FileContent.BackgroundBrush", cardBrush.Color,
                    glass.SurfaceOpacity == 1 ? ShellStyleKind.Layered : style,
                    GlassMaterialPolicy.ContentCoverage(glass.SurfaceOpacity), neutral);
            Set(theme, "FilesMate.Tab.BackgroundBrush", dark ? Color.FromArgb(255, 58, 58, 60) : Color.FromArgb(255, 242, 242, 247), style);
            Set(theme, "FilesMate.Tab.SelectedBrush", dark ? Color.FromArgb(255, 72, 72, 74) : Colors.White, style);
        }
        foreach (var merged in resources.MergedDictionaries) Apply(merged, settings);
    }

    private static void ApplyFloatingSurfaces(ResourceDictionary theme, bool dark, double surfaceOpacity)
    {
        // A raised surface belongs to the same cool-neutral palette as the shell.
        // Keep a readable tint over blurred content instead of fading the whole
        // popup (which would also expose sharp text underneath).
        var color = dark ? Color.FromArgb(255, 53, 59, 67) : Color.FromArgb(255, 250, 251, 253);
        foreach (var key in new[] { "FilesMate.Menu.BackgroundBrush", "FilesMate.LiquidGlass.FillBrush",
            "FilesMate.InfoPane.BackgroundBrush", "FilesMate.App.BackgroundBrush" })
        {
            if (!theme.TryGetValue(key, out var value) || value is not AcrylicBrush brush) continue;
            brush.TintColor = color;
            brush.FallbackColor = color;
            brush.TintOpacity = GlassMaterialPolicy.FloatingCoverage(surfaceOpacity);
            brush.TintLuminosityOpacity = dark ? 0.55 : 0.65;
            brush.AlwaysUseFallback = surfaceOpacity == 1;
        }
        foreach (var key in new[] { "FilesMate.LiquidGlass.SolidFillBrush", "FilesMate.Update.SurfaceBrush" })
            if (theme.TryGetValue(key, out var value) && value is SolidColorBrush brush) brush.Color = color;
        foreach (var key in new[] { "FilesMate.SettingsPanel.BackgroundBrush", "ContentDialogBackground",
            "ComboBoxDropDownBackground", "FilesMate.SearchPanel.BackgroundBrush" })
            Paint(theme, key, color);
        var card = dark ? Color.FromArgb(255, 61, 67, 75) : Colors.White;
        var cardCoverage = GlassMaterialPolicy.CardCoverage(surfaceOpacity);
        Paint(theme, "FilesMate.SettingsCard.BackgroundBrush", ContrastTint(card, dark ? color : Colors.White, cardCoverage), cardCoverage);
        Paint(theme, "ContentDialogTopOverlay", card);
        Paint(theme, "FilesMate.SettingsOverlay.ScrimBrush", Color.FromArgb(dark ? (byte)0x26 : (byte)0x1A, 0, 0, 0));
        var nav = dark ? Color.FromArgb(255, 42, 47, 54) : Color.FromArgb(255, 240, 244, 248);
        Paint(theme, "FilesMate.SettingsNav.BackgroundBrush", ContrastTint(nav, dark ? color : Colors.White, cardCoverage), cardCoverage);
    }

    private static void Paint(ResourceDictionary theme, string key, Color color, double opacity = 1)
    {
        if (theme.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        { brush.Color = color; brush.Opacity = opacity; }
    }

    private static Color ContrastTint(Color color, Color neutral, double coverage) => Color.FromArgb(color.A,
        GlassMaterialPolicy.ContrastTint(color.R, neutral.R, coverage),
        GlassMaterialPolicy.ContrastTint(color.G, neutral.G, coverage),
        GlassMaterialPolicy.ContrastTint(color.B, neutral.B, coverage));

    private static void Set(ResourceDictionary theme, string key, Color unified, ShellStyleKind style, double opacity = 1, Color? neutral = null)
    {
        if (!theme.TryGetValue(key, out var value) || value is not SolidColorBrush brush) return;
        LayeredColors.TryAdd(brush, brush.Color);
        brush.Color = style == ShellStyleKind.Unified ? unified : neutral is { } anchor
            ? ContrastTint(LayeredColors[brush], anchor, opacity) : LayeredColors[brush];
        brush.Opacity = opacity;
    }
}
