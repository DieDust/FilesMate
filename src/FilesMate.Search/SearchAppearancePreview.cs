namespace FilesMate.Search;

/// <summary>Only appearance tokens cross the existing current-user profile pipe.</summary>
public sealed record SearchAppearancePreview(
    string Theme, string Backdrop, string ReduceMotion, string GlassEffect,
    string Accent, string? CustomAccent, int TransparencyPercent, string? SkinJson);
