using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using FilesMate.App.Models;
using Microsoft.Win32;

namespace FilesMate.SearchHost;

internal static class PaletteAppearance
{
    private static readonly Lazy<XDocument> ThemeTokens = new(() =>
    {
        using var stream = typeof(PaletteAppearance).Assembly.GetManifestResourceStream("FilesMate.ThemeColors")!;
        return XDocument.Load(stream);
    });
    private static readonly Lazy<XDocument> GeometryTokens = new(() =>
    {
        using var stream = typeof(PaletteAppearance).Assembly.GetManifestResourceStream("FilesMate.ThemeMetrics")!;
        return XDocument.Load(stream);
    });
    internal static bool UseGlass(AppearanceSettings settings)
    {
        if (SystemParameters.HighContrast || settings.GlassEffect == GlassEffectMode.Off || settings.Backdrop == BackdropKind.Solid || settings.EffectiveTransparencyPercent == 0) return false;
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is not int enabled || enabled != 0; }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return false; }
    }
    internal static AppearanceSettings Load(string profile)
    {
        try
        {
            var path = Path.Combine(profile, "appearance.json");
            // A settings file is small. Do not let a malformed profile allocate
            // an unbounded document in the resident search process.
            if (new FileInfo(path).Length > 256 * 1024) return AppearanceSettings.Default;
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 24 });
            string? Read(string name) => document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            int? transparency = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("transparencyPercent", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var percent) ? percent : null;
            bool? bundled = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("useBundledFileIcons", out var icons) && icons.ValueKind is JsonValueKind.True or JsonValueKind.False ? icons.GetBoolean() : null;
            SkinPackage? skin = null;
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("skin", out var package)
                && package.ValueKind == JsonValueKind.Object)
                SkinPackageSerializer.TryDeserialize(package.GetRawText(), out skin, out _);
            return AppearanceSettings.Sanitize(Read("theme"), Read("backdrop"), null, null, Read("reduceMotion"), Read("glassEffect"), Read("accent"), Read("customAccent"), transparency, bundled, fileFontFamily: Read("fileFontFamily"), skin: skin);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return AppearanceSettings.Default; }
    }

    internal static bool IsDark(AppearanceSettings settings)
    {
        if (settings.Theme != AppThemeKind.System) return settings.Theme == AppThemeKind.Dark;
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0; }
        catch (Exception error) when (error is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return false; }
    }

    internal static void Apply(ResourceDictionary resources, AppearanceSettings settings)
    {
        ApplyGeometry(resources);
        resources["AppFontFamily"] = new FontFamily(settings.FileFontFamily ?? "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        if (SystemParameters.HighContrast)
        {
            resources["Surface"] = SystemColors.WindowBrush;
            resources["MenuSurface"] = SystemColors.WindowBrush;
            resources["GlassSurface"] = resources["PaletteCanvas"] = resources["ResultSurface"] = SystemColors.WindowBrush;
            resources["PreviewSurface"] = SystemColors.WindowBrush;
            resources["MenuHover"] = SystemColors.HighlightBrush;
            resources["MenuPressed"] = SystemColors.HighlightBrush;
            resources["MenuActiveInk"] = SystemColors.HighlightTextBrush;
            resources["ResultHover"] = SystemColors.ControlBrush;
            resources["SelectionBorder"] = SystemColors.HighlightTextBrush;
            resources["ScrollThumb"] = resources["ScrollThumbActive"] = SystemColors.WindowTextBrush;
            resources["Accent"] = SystemColors.HighlightBrush;
            resources["OnAccent"] = SystemColors.HighlightTextBrush;
            resources["ToggleTrack"] = SystemColors.HighlightBrush;
            resources["ToggleOff"] = SystemColors.WindowBrush;
            resources["DisabledInk"] = SystemColors.GrayTextBrush;
            resources["PillSurface"] = SystemColors.ControlBrush;
            foreach (var key in new[] { "Ink", "Muted", "Line", "Outline", "MatchInk" }) resources[key] = SystemColors.WindowTextBrush;
            resources["Selected"] = SystemColors.HighlightBrush;
            resources["SelectedHover"] = SystemColors.HighlightBrush;
            resources["ButtonHover"] = SystemColors.HighlightBrush;
            resources["ButtonActiveInk"] = SystemColors.HighlightTextBrush;
            resources["Hover"] = SystemColors.ControlBrush;
            resources["CloseHover"] = resources["ClosePressed"] = SystemColors.HighlightBrush;
            resources["CloseInk"] = SystemColors.HighlightTextBrush;
            resources["SelectedInk"] = resources["SelectedMuted"] = SystemColors.HighlightTextBrush;
            return;
        }
        var dark = IsDark(settings);
        var skin = SkinPalette.For(dark, settings);
        // Skin and text roles share one palette with WinUI. Functional colors
        // stay in the shared theme dictionary, independent of skin replacement.
        foreach (var (key, color) in skin.CompanionColors())
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AccentPalette.ToHex(color)));
        resources["MatchInk"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AccentPalette.ToHex(
            TextContrast(skin.Accent, skin.Content) >= 4.5 ? skin.Accent : skin.Text)));
        var document = ThemeTokens.Value;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var theme = document.Descendants().First(element => element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(x + "Key") == (dark ? "Dark" : "Light"));
        var mappings = new Dictionary<string, string>
        {
            ["CloseHover"] = "FilesMate.Close.HoverBrush", ["ClosePressed"] = "FilesMate.Close.PressedBrush",
        };
        foreach (var (key, token) in mappings)
        {
            var value = BrushColor(document, theme, token);
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
        var canvasColor = ((SolidColorBrush)resources["PaletteCanvas"]).Color;
        resources["CloseInk"] = Brushes.White;
        if (UseGlass(settings))
        {
            // Native backdrops supply blur; tint only surfaces, never foreground.
            var opacity = 1 - settings.EffectiveTransparencyPercent / 100d;
            var floating = FilesMate.App.Animations.GlassMaterialPolicy.FloatingCoverage(opacity);
            resources["GlassSurface"] = Tint(canvasColor, floating);
            var floatingColor = (Color)ColorConverter.ConvertFromString(AccentPalette.ToHex(skin.Floating));
            resources["MenuSurface"] = resources["Surface"] = resources["PreviewSurface"] = Tint(floatingColor, floating);
            resources["ResultSurface"] = Tint((Color)ColorConverter.ConvertFromString(AccentPalette.ToHex(skin.Content)),
                FilesMate.App.Animations.GlassMaterialPolicy.ContentCoverage(opacity));
            resources["PillSurface"] = Tint((Color)ColorConverter.ConvertFromString(AccentPalette.ToHex(skin.Card)),
                FilesMate.App.Animations.GlassMaterialPolicy.CardCoverage(opacity));
        }
        else resources["GlassSurface"] = resources["PaletteCanvas"];
    }

    private static void ApplyGeometry(ResourceDictionary resources)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string Token(string key) => GeometryTokens.Value.Descendants().Single(element => (string?)element.Attribute(x + "Key") == key).Value;
        foreach (var (key, token) in new[] { ("SmallCorner", "FilesMate.Corner.Small"), ("SurfaceCorner", "FilesMate.Corner.Card"),
            ("InputCorner", "FilesMate.Corner.Omnibar"), ("PillCorner", "FilesMate.Corner.Pill") })
            resources[key] = (CornerRadius)new CornerRadiusConverter().ConvertFromInvariantString(Token(token))!;
        resources["MenuAcceleratorGap"] = (Thickness)new ThicknessConverter().ConvertFromInvariantString(Token("FilesMate.Menu.AcceleratorGap"))!;
        resources["MenuChevronGap"] = (Thickness)new ThicknessConverter().ConvertFromInvariantString(Token("FilesMate.Menu.ChevronGap"))!;
        resources["MenuLeadingSlot"] = new GridLength(double.Parse(Token("FilesMate.Menu.LeadingSlot"), System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static int NativeBackdrop(AppearanceSettings settings) => !UseGlass(settings) ? 1 : settings.Backdrop switch
    {
        BackdropKind.Mica => 2,
        BackdropKind.MicaAlt => 4,
        _ => 3,
    };

    private static SolidColorBrush Tint(Color color, double coverage)
    {
        color.A = (byte)Math.Round(255 * coverage);
        return new SolidColorBrush(color);
    }

    private static double TextContrast(uint first, uint second)
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
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static string BrushColor(XDocument document, XElement theme, string key)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(key))
        {
            var resource = theme.Elements().Concat(document.Root!.Elements())
                .First(element => (string?)element.Attribute(x + "Key") == key);
            if (resource.Attribute("Color") is { } color) return color.Value;
            key = resource.Attribute("ResourceKey")?.Value
                ?? throw new InvalidDataException($"Theme brush '{key}' has no color.");
        }
        throw new InvalidDataException($"Theme brush '{key}' contains a resource cycle.");
    }
}
