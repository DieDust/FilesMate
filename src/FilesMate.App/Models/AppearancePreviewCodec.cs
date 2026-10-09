using System.Text.Json;
using FilesMate.Search;

namespace FilesMate.App.Models;

public static class AppearancePreviewCodec
{
    public static SearchAppearancePreview Encode(AppearanceSettings settings) => new(
        settings.Theme.ToString(), settings.Backdrop.ToString(), settings.ReduceMotion.ToString(),
        settings.GlassEffect.ToString(), settings.Accent.ToString(), settings.CustomAccent,
        settings.EffectiveTransparencyPercent,
        settings.Skin is null ? null : SkinPackageSerializer.Serialize(settings.Skin));

    public static bool TryDecode(SearchAppearancePreview preview, out AppearanceSettings? settings)
    {
        settings = null;
        if (!Parse(preview.Theme, out AppThemeKind theme) || !Parse(preview.Backdrop, out BackdropKind backdrop)
            || !Parse(preview.ReduceMotion, out ReduceMotionKind motion) || !Parse(preview.GlassEffect, out GlassEffectMode glass)
            || !Parse(preview.Accent, out AccentKind accent) || preview.TransparencyPercent is < 0 or > 100
            || preview.CustomAccent is not null && (preview.CustomAccent.Length > 9 || !AccentPalette.TryParse(preview.CustomAccent, out _))) return false;
        try
        {
            var skin = preview.SkinJson is null ? null : SkinPackageSerializer.Deserialize(preview.SkinJson);
            settings = AppearanceSettings.Default with { Theme = theme, Backdrop = backdrop, ReduceMotion = motion,
                GlassEffect = glass, Accent = accent, CustomAccent = preview.CustomAccent,
                TransparencyPercent = preview.TransparencyPercent, Skin = skin };
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException) { return false; }
    }

    public static AppearanceSettings Overlay(AppearanceSettings committed, AppearanceSettings preview) =>
        committed with { Theme = preview.Theme, Backdrop = preview.Backdrop,
            ReduceMotion = preview.ReduceMotion, GlassEffect = preview.GlassEffect, Accent = preview.Accent,
            CustomAccent = preview.CustomAccent, TransparencyPercent = preview.TransparencyPercent, Skin = preview.Skin };

    public static bool VisuallyEquivalent(AppearanceSettings left, AppearanceSettings right) =>
        left with { Skin = null } == right with { Skin = null }
        && left.Skin?.Light == right.Skin?.Light && left.Skin?.Dark == right.Skin?.Dark;

    private static bool Parse<T>(string? value, out T result) where T : struct, Enum
    {
        result = default;
        return value is { Length: > 0 and <= 24 } && !int.TryParse(value, out _)
            && Enum.TryParse(value, ignoreCase: false, out result) && Enum.GetName(result) == value;
    }
}
