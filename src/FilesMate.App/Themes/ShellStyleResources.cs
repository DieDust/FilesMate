using FilesMate.App.Models;
using FilesMate.App.Animations;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FilesMate.App.Themes;

internal static class ShellStyleResources
{
    // Keep the declared layered palette so repeated appearance changes never accumulate changes.
    // Mutating the existing brushes also updates loaded tabs without recreating their views.
    private static readonly Dictionary<SolidColorBrush, Color> LayeredColors = new();

    public static void Apply(ResourceDictionary resources, AppearanceSettings settings)
    {
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
            var neutral = FromArgb(dark ? SurfacePalette.Foundation(true) : SurfacePalette.Card(false));
            Set(theme, "FilesMate.Chrome.FillBrush",
                GlassMaterialPolicy.FloatingCoverage(glass.SurfaceOpacity), neutral);
            Set(theme, "FilesMate.Sidebar.BackgroundBrush", surfaceOpacity, neutral);
            Set(theme, "FilesMate.CommandBar.BackgroundBrush", surfaceOpacity, neutral);
            Set(theme, "FilesMate.FileArea.BackgroundBrush",
                GlassMaterialPolicy.FoundationCoverage(glass.SurfaceOpacity));
            Set(theme, "FilesMate.Favorites.BackgroundBrush", surfaceOpacity, neutral);
            Set(theme, "FilesMate.Shell.SeparatorBrush");
            Set(theme, "FilesMate.FileContent.BorderBrush");
            Set(theme, "FilesMate.FileContent.BackgroundBrush",
                GlassMaterialPolicy.ContentCoverage(glass.SurfaceOpacity), neutral);
            Set(theme, "FilesMate.Tab.BackgroundBrush");
            Set(theme, "FilesMate.Tab.SelectedBrush");
        }
        foreach (var merged in resources.MergedDictionaries) Apply(merged, settings);
    }

    private static void ApplyFloatingSurfaces(ResourceDictionary theme, bool dark, double surfaceOpacity)
    {
        // Raised surfaces share a palette with the companion search window.
        // Keep a readable tint over blurred content instead of fading the whole
        // popup (which would also expose sharp text underneath).
        var color = FromArgb(SurfacePalette.Floating(dark));
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
        var card = FromArgb(SurfacePalette.Card(dark));
        var cardCoverage = GlassMaterialPolicy.CardCoverage(surfaceOpacity);
        Paint(theme, "FilesMate.SettingsCard.BackgroundBrush", ContrastTint(card, dark ? color : card, cardCoverage), cardCoverage);
        Paint(theme, "ContentDialogTopOverlay", card);
        Paint(theme, "FilesMate.SettingsOverlay.ScrimBrush", Color.FromArgb(dark ? (byte)0x26 : (byte)0x1A, 0, 0, 0));
        var nav = FromArgb(SurfacePalette.Navigation(dark));
        Paint(theme, "FilesMate.SettingsNav.BackgroundBrush", ContrastTint(nav, dark ? color : card, cardCoverage), cardCoverage);
    }

    private static void Paint(ResourceDictionary theme, string key, Color color, double opacity = 1)
    {
        if (theme.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        { brush.Color = color; brush.Opacity = opacity; }
    }

    internal static Color FromArgb(uint value) => Color.FromArgb((byte)(value >> 24),
        (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private static Color ContrastTint(Color color, Color neutral, double coverage) => Color.FromArgb(color.A,
        GlassMaterialPolicy.ContrastTint(color.R, neutral.R, coverage),
        GlassMaterialPolicy.ContrastTint(color.G, neutral.G, coverage),
        GlassMaterialPolicy.ContrastTint(color.B, neutral.B, coverage));

    private static void Set(ResourceDictionary theme, string key, double opacity = 1, Color? neutral = null)
    {
        if (!theme.TryGetValue(key, out var value) || value is not SolidColorBrush brush) return;
        LayeredColors.TryAdd(brush, brush.Color);
        brush.Color = neutral is { } anchor
            ? ContrastTint(LayeredColors[brush], anchor, opacity) : LayeredColors[brush];
        brush.Opacity = opacity;
    }
}
