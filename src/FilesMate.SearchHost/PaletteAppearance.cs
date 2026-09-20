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
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(profile, "appearance.json")));
            string? Read(string name) => document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            int? transparency = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("transparencyPercent", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var percent) ? percent : null;
            bool? bundled = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("useBundledFileIcons", out var icons) && icons.ValueKind is JsonValueKind.True or JsonValueKind.False ? icons.GetBoolean() : null;
            return AppearanceSettings.Sanitize(Read("theme"), Read("backdrop"), null, null, Read("reduceMotion"), Read("glassEffect"), Read("accent"), Read("customAccent"), Read("shellStyle"), transparency, bundled);
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
        if (SystemParameters.HighContrast)
        {
            resources["Surface"] = SystemColors.WindowBrush;
            resources["MenuSurface"] = SystemColors.WindowBrush;
            resources["GlassSurface"] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Ink", "Muted", "Line", "Accent" }) resources[key] = SystemColors.WindowTextBrush;
            resources["Selected"] = SystemColors.HighlightBrush;
            resources["Hover"] = SystemColors.ControlBrush;
            resources["CloseHover"] = resources["ClosePressed"] = SystemColors.HighlightBrush;
            resources["CloseInk"] = SystemColors.HighlightTextBrush;
            resources["SelectedInk"] = resources["SelectedMuted"] = SystemColors.HighlightTextBrush;
            return;
        }
        var dark = IsDark(settings);
        // Consume the main app's actual theme tokens instead of maintaining a
        // second palette that drifts from its Light/Dark colors.
        var document = ThemeTokens.Value;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var theme = document.Descendants().First(element => element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(x + "Key") == (dark ? "Dark" : "Light"));
        var mappings = new Dictionary<string, string>
        {
            ["Surface"] = "FilesMate.SearchPanel.BackgroundBrush", ["Ink"] = "FilesMate.Text.PrimaryBrush",
            ["Muted"] = "FilesMate.Text.SecondaryBrush", ["Line"] = "FilesMate.Divider.Brush",
            ["Selected"] = "FilesMate.Item.SelectedBrush", ["Hover"] = "FilesMate.Item.HoverBrush",
            ["CloseHover"] = "FilesMate.Close.HoverBrush", ["ClosePressed"] = "FilesMate.Close.PressedBrush",
        };
        foreach (var (key, token) in mappings)
        {
            var value = theme.Elements().First(element => (string?)element.Attribute(x + "Key") == token).Attribute("Color")!.Value;
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
        var menuColor = dark ? "#353B43" : "#FAFBFD";
        resources["MenuSurface"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(menuColor));
        resources["Surface"] = resources["MenuSurface"];
        var accent = AccentPalette.Resolve(settings.Accent, settings.CustomAccent, dark);
        resources["Accent"] = new SolidColorBrush(Color.FromArgb(255, (byte)(accent >> 16), (byte)(accent >> 8), (byte)accent));
        if (settings.Accent != AccentKind.Default)
            resources["Selected"] = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x47 : (byte)0x24, (byte)(accent >> 16), (byte)(accent >> 8), (byte)accent));
        resources["CloseInk"] = Brushes.White;
        resources["SelectedInk"] = resources["Ink"];
        resources["SelectedMuted"] = resources["Muted"];
        if (UseGlass(settings))
        {
            // Native desktop acrylic supplies blur; this is the shared neutral tint above it.
            var color = (Color)ColorConverter.ConvertFromString(menuColor);
            color.A = (byte)Math.Round(255 * FilesMate.App.Animations.GlassMaterialPolicy.FloatingCoverage(1 - settings.EffectiveTransparencyPercent / 100d));
            resources["GlassSurface"] = new SolidColorBrush(color);
        }
        else resources["GlassSurface"] = resources["Surface"];
    }
}
