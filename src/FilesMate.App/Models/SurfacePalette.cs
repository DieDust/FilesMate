namespace FilesMate.App.Models;

/// <summary>Shared opaque surfaces for the WinUI shell and WPF companion windows.</summary>
public static class SurfacePalette
{
    public static uint Floating(bool dark) => SkinPalette.For(dark).Floating;
    public static uint Card(bool dark) => SkinPalette.For(dark).Card;
    public static uint Navigation(bool dark) => SkinPalette.For(dark).Navigation;
    public static uint Foundation(bool dark) => SkinPalette.For(dark).Canvas;
    public static byte SelectionAlpha(bool dark) => SkinPalette.For(dark).SelectionAlpha;
    public static byte SelectionHoverAlpha(bool dark) => SkinPalette.For(dark).SelectionHoverAlpha;
}
