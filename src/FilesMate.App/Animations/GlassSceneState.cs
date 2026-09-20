using FilesMate.App.Models;

namespace FilesMate.App.Animations;

public readonly record struct GlassSceneState(
    bool UseStaticGlass,
    double SurfaceOpacity,
    double HighlightOpacity,
    double ShadowOpacity)
{
    public static GlassSceneState Resolve(AppearanceSettings settings)
    {
        var state = Resolve(settings.GlassEffect, true, true, false);
        return state with
        {
            SurfaceOpacity = settings.Backdrop == BackdropKind.Solid || settings.GlassEffect == GlassEffectMode.Off
                ? 1 : 1 - settings.EffectiveTransparencyPercent / 100d,
        };
    }

    public static GlassSceneState Resolve(
        GlassEffectMode mode,
        bool systemAnimationsEnabled,
        bool windowActive,
        bool remoteSession) => mode switch
    {
        GlassEffectMode.Off => new GlassSceneState(
            UseStaticGlass: false,
            SurfaceOpacity: 1,
            HighlightOpacity: 0,
            ShadowOpacity: 0.08),
        GlassEffectMode.Immersive => new GlassSceneState(
            UseStaticGlass: true,
            SurfaceOpacity: 0.68,
            HighlightOpacity: 0.72,
            ShadowOpacity: 0.28),
        _ => new GlassSceneState(
            UseStaticGlass: true,
            SurfaceOpacity: 0.82,
            HighlightOpacity: 0.56,
            ShadowOpacity: 0.18),
    };
}
