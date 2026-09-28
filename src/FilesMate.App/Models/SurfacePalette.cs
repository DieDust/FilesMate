namespace FilesMate.App.Models;

/// <summary>Shared opaque surfaces for the WinUI shell and WPF companion windows.</summary>
public static class SurfacePalette
{
    public static uint Floating(bool dark) => dark ? 0xFF2C2C2C : 0xFFFAF9F5;
    public static uint Card(bool dark) => dark ? 0xFF323232 : 0xFFFFFFFF;
    public static uint Navigation(bool dark) => dark ? 0xFF202020 : 0xFFF2F0E9;
    public static uint Foundation(bool dark) => dark ? 0xFF242424 : 0xFFF8F6F0;
    public static byte SelectionAlpha(bool dark) => dark ? (byte)0x20 : (byte)0x1C;
    public static byte SelectionHoverAlpha(bool dark) => dark ? (byte)0x2E : (byte)0x2B;
}
