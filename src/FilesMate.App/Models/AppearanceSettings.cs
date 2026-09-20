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

public enum ShellStyleKind
{
    Layered,
    Unified,
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
    ShellStyleKind ShellStyle = ShellStyleKind.Layered,
    int? TransparencyPercent = null,
    bool UseBundledFileIcons = true)
{
    public int EffectiveTransparencyPercent => Math.Clamp(TransparencyPercent ?? (GlassEffect switch
    {
        GlassEffectMode.Off => 0,
        GlassEffectMode.Immersive => 32,
        _ => 18,
    }), 0, 100);

    public static AppearanceSettings Default { get; } = new(
        AppThemeKind.System,
        BackdropKind.Acrylic,
        ShowStatusBar: true,
        ShowToolbar: true,
        ReduceMotionKind.System,
        GlassEffectMode.Balanced,
        AccentKind.Default,
        CustomAccent: null);

    public static AppearanceSettings Sanitize(
        string? theme,
        string? backdrop,
        bool? showStatusBar,
        bool? showToolbar,
        string? reduceMotion,
        string? glassEffect = null,
        string? accent = null,
        string? customAccent = null,
        string? shellStyle = null,
        int? transparencyPercent = null,
        bool? useBundledFileIcons = null) =>
        new(
            Parse(theme, AppThemeKind.System),
            Parse(backdrop, BackdropKind.Acrylic),
            showStatusBar ?? Default.ShowStatusBar,
            showToolbar ?? Default.ShowToolbar,
            Parse(reduceMotion, ReduceMotionKind.System),
            Parse(glassEffect, GlassEffectMode.Balanced),
            Parse(accent, AccentKind.Default),
            AccentPalette.TryParse(customAccent, out var parsed) ? AccentPalette.ToHex(parsed) : null,
            Parse(shellStyle, ShellStyleKind.Layered),
            transparencyPercent is { } percent ? Math.Clamp(percent, 0, 100) : null,
            useBundledFileIcons ?? true);

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
