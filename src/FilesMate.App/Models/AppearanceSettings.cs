using System.Linq;

namespace FilesMate.App.Models;

public enum AppThemeKind
{
    System,
    Light,
    Dark,
}

public enum BackdropKind
{
    Acrylic,
    Mica,
    MicaAlt,
    Solid,
}

public enum ReduceMotionKind
{
    System,
    On,
}

public enum GlassEffectMode
{
    Off,
    Balanced,
    Immersive,
}

public sealed record AppearanceSettings(
    AppThemeKind Theme,
    BackdropKind Backdrop,
    bool ShowStatusBar,
    bool ShowToolbar,
    ReduceMotionKind ReduceMotion,
    GlassEffectMode GlassEffect,
    AccentKind Accent = AccentKind.Default,
    string? CustomAccent = null,
    int? TransparencyPercent = null,
    bool UseBundledFileIcons = true,
    string? FileFontFamily = null,
    double FileNameFontSize = 13,
    double FileDetailsFontSize = 12,
    SkinPackage? Skin = null)
{
    public double FileRowHeight => Math.Max(28, Math.Ceiling(Math.Max(FileNameFontSize, FileDetailsFontSize) * 1.6) + 6);
    public double FileRowHeightForScale(double scale) => double.IsFinite(scale) && scale > 0
        ? Math.Ceiling(FileRowHeight * scale) / scale : FileRowHeight;
    public static string? SanitizeFont(string? font) => string.IsNullOrWhiteSpace(font) || font.Length > 100
        || font.Any(char.IsControl) || font.IndexOfAny(['/', '\\', '#', ':']) >= 0 ? null : font.Trim();
    public static double SanitizeFontSize(double size, double fallback, double maximum) =>
        double.IsFinite(size) ? Math.Clamp(Math.Round(size), 10, maximum) : fallback;
    public int EffectiveTransparencyPercent => Math.Clamp(TransparencyPercent ?? PresetTransparencyPercent(GlassEffect), 0, 100);
    public static int PresetTransparencyPercent(GlassEffectMode effect) => effect switch
    {
        GlassEffectMode.Off => 0,
        GlassEffectMode.Immersive => 32,
        _ => 18,
    };

    public static AppearanceSettings Default { get; } = new(
        AppThemeKind.System,
        BackdropKind.Acrylic,
        ShowStatusBar: true,
        ShowToolbar: true,
        ReduceMotionKind.System,
        GlassEffectMode.Balanced,
        AccentKind.Default,
        CustomAccent: null,
        TransparencyPercent: 0);

    public static AppearanceSettings Sanitize(
        string? theme,
        string? backdrop,
        bool? showStatusBar,
        bool? showToolbar,
        string? reduceMotion,
        string? glassEffect = null,
        string? accent = null,
        string? customAccent = null,
        int? transparencyPercent = null,
        bool? useBundledFileIcons = null,
        string? fileFontFamily = null,
        double? fileNameFontSize = null,
        double? fileDetailsFontSize = null,
        SkinPackage? skin = null) =>
        new(
            Parse(theme, AppThemeKind.System),
            Parse(backdrop, BackdropKind.Acrylic),
            showStatusBar ?? Default.ShowStatusBar,
            showToolbar ?? Default.ShowToolbar,
            Parse(reduceMotion, ReduceMotionKind.System),
            Parse(glassEffect, GlassEffectMode.Balanced),
            Parse(accent, AccentKind.Default),
            AccentPalette.TryParse(customAccent, out var parsed) ? AccentPalette.ToHex(parsed) : null,
            SanitizeTransparency(backdrop, glassEffect, transparencyPercent),
            useBundledFileIcons ?? true,
            SanitizeFont(fileFontFamily),
            SanitizeFontSize(fileNameFontSize ?? 13, 13, 24),
            SanitizeFontSize(fileDetailsFontSize ?? 12, 12, 20),
            skin);

    private static int? SanitizeTransparency(string? backdrop, string? glassEffect, int? transparencyPercent)
    {
        if (transparencyPercent is { } percent) return Math.Clamp(percent, 0, 100);
        // Explicit pre-slider presets/materials are existing choices. Keep their
        // historical percentage; only profiles with no glass choice start opaque.
        if (!string.IsNullOrWhiteSpace(glassEffect) && !int.TryParse(glassEffect.Trim(), out _)
            && Enum.TryParse<GlassEffectMode>(glassEffect.Trim(), ignoreCase: true, out var effect)
            && Enum.IsDefined(effect)) return null;
        if (!string.IsNullOrWhiteSpace(backdrop) && !int.TryParse(backdrop.Trim(), out _)
            && Enum.TryParse<BackdropKind>(backdrop.Trim(), ignoreCase: true, out var material)
            && Enum.IsDefined(material) && material != BackdropKind.Solid) return null;
        return Default.TransparencyPercent;
    }

    private static TEnum Parse<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value.Trim(), out _))
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? parsed
            : fallback;
    }
}
