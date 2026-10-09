using System.Collections.ObjectModel;
using System.Globalization;

namespace FilesMate.App.Models;

public enum SkinColorRole
{
    Accent,
    Background,
    Text,
    Selection,
}

/// <summary>Opaque RGB tokens; material opacity belongs to appearance settings.</summary>
public sealed record SkinColors(
    uint Canvas, uint Navigation, uint Chrome, uint Content, uint Card, uint Floating,
    uint Input, uint Text, uint Muted, uint Line, uint Accent, uint Selection)
{
    public SkinPalette Resolve(bool dark) => new()
    {
        Dark = dark, Canvas = Canvas, Navigation = Navigation, Chrome = Chrome,
        Content = Content, Card = Card, Floating = Floating, Input = Input,
        Text = Text, Muted = Muted, Line = Line, Accent = Accent, Selection = Selection,
    };

    public static SkinColors FromPalette(SkinPalette palette) => new(
        palette.Canvas, palette.Navigation, palette.Chrome, palette.Content,
        palette.Card, palette.Floating, palette.Input, palette.Text, palette.Muted,
        palette.Line, palette.Accent | 0xFF000000,
        palette.Selection ?? SkinPalette.Mix(palette.Accent, palette.Content, palette.SelectionAlpha / 255d));

    public static bool TryParseRgb(string? value, out uint rgb)
    {
        rgb = 0;
        if (value is null || value.Length != 7 || value[0] != '#') return false;
        foreach (var character in value.AsSpan(1))
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F'))) return false;
        if (!uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)) return false;
        rgb = 0xFF000000 | parsed;
        return true;
    }

    public static string ToHex(uint rgb) => $"#{rgb & 0xFFFFFF:X6}";

    internal void Validate()
    {
        foreach (var color in new[] { Canvas, Navigation, Chrome, Content, Card, Floating, Input, Text, Muted, Line, Accent, Selection })
            if ((color >> 24) != 0xFF) throw new ArgumentException("Skin colors must be opaque RGB values.");
    }

    internal SkinColors WithColor(bool dark, SkinColorRole role, uint rgb)
    {
        if ((rgb >> 24) != 0xFF) throw new ArgumentException("Skin colors must be opaque RGB values.", nameof(rgb));
        return role switch
        {
            SkinColorRole.Accent => this with { Accent = rgb },
            SkinColorRole.Selection => this with { Selection = rgb },
            SkinColorRole.Text => this with
            {
                Text = rgb,
                Muted = SkinPalette.Mix(rgb, Content, dark ? .70 : .75),
                Line = rgb,
            },
            // A background edit coordinates every surface, retaining the requested hue.
            // The text and selection remain independent choices and are checked below.
            SkinColorRole.Background => this with
            {
                Canvas = rgb,
                Navigation = SkinPalette.Mix(0xFF000000, rgb, dark ? .12 : .025),
                Chrome = SkinPalette.Mix(0xFF000000, rgb, dark ? .20 : .035),
                Content = SkinPalette.Mix(0xFFFFFFFF, rgb, dark ? .045 : .42),
                Card = SkinPalette.Mix(0xFFFFFFFF, rgb, dark ? .065 : .48),
                Floating = SkinPalette.Mix(0xFFFFFFFF, rgb, dark ? .035 : .20),
                Input = SkinPalette.Mix(dark ? 0xFFFFFFFF : 0xFF000000, rgb, dark ? .065 : .015),
                Muted = SkinPalette.Mix(Text, rgb, dark ? .70 : .75),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }
}

public readonly record struct SkinContrastWarning(bool Dark, string Role, double Ratio, double Minimum);

/// <summary>A portable color package. It contains no settings, paths or executable content.</summary>
public sealed record SkinPackage
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; }
    public string Name { get; }
    public SkinColors Light { get; }
    public SkinColors Dark { get; }

    public SkinPackage(int schemaVersion, string name, SkinColors light, SkinColors dark)
    {
        if (schemaVersion != CurrentSchemaVersion) throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Unsupported skin schema version.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Skin name must contain 1–80 readable characters.", nameof(name));
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(dark);
        light.Validate();
        dark.Validate();
        SchemaVersion = schemaVersion;
        Name = name.Trim();
        Light = light;
        Dark = dark;
    }

    public SkinPalette Resolve(bool dark) => (dark ? Dark : Light).Resolve(dark);
    public SkinPackage WithName(string name) => new(SchemaVersion, name, Light, Dark);
    public SkinPackage WithColor(bool dark, SkinColorRole role, uint rgb) => dark
        ? new(SchemaVersion, Name, Light, Dark.WithColor(true, role, rgb))
        : new(SchemaVersion, Name, Light.WithColor(false, role, rgb), Dark);

    public static SkinPackage FromAppearance(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Skin ?? new(CurrentSchemaVersion, "Custom",
            SkinColors.FromPalette(SkinPalette.For(false, settings)),
            SkinColors.FromPalette(SkinPalette.For(true, settings)));
    }

    public IReadOnlyList<SkinContrastWarning> GetContrastWarnings()
    {
        var warnings = new List<SkinContrastWarning>();
        foreach (var dark in new[] { false, true })
        {
            var colors = dark ? Dark : Light;
            void Check(string role, uint ink, uint surface, double minimum)
            {
                var ratio = SkinPalette.ContrastRatio(ink, surface);
                if (ratio < minimum) warnings.Add(new(dark, role, ratio, minimum));
            }
            var surfaces = new[] { colors.Canvas, colors.Navigation, colors.Chrome, colors.Content, colors.Card, colors.Floating, colors.Input };
            var normalRatio = surfaces.Min(surface => SkinPalette.ContrastRatio(colors.Text, surface));
            if (normalRatio < 4.5) warnings.Add(new(dark, "normalText", normalRatio, 4.5));
            var mutedRatio = surfaces.Min(surface => SkinPalette.ContrastRatio(colors.Muted, surface));
            if (mutedRatio < 4.5) warnings.Add(new(dark, "mutedText", mutedRatio, 4.5));
            Check("selectedText", colors.Text, colors.Selection, 4.5);
        }
        return warnings.AsReadOnly();
    }
}

public sealed record SkinPreset(string Id, string Name, SkinPackage Package);

public static class SkinPresets
{
    public static SkinPackage WarmWhite { get; } = new(SkinPackage.CurrentSchemaVersion, "Warm White",
        SkinColors.FromPalette(SkinPalette.Light), SkinColors.FromPalette(SkinPalette.DarkSkin));
    public static SkinPackage Graphite { get; } = new(SkinPackage.CurrentSchemaVersion, "Graphite",
        new(0xFFF4F5F6, 0xFFEAEDF0, 0xFFE5E8EC, 0xFFFFFFFF, 0xFFFDFDFE, 0xFFF8F9FA, 0xFFEEF0F3,
            0xFF242A32, 0xFF535D6A, 0xFF34404F, 0xFF47627F, 0xFFDDE7F2),
        new(0xFF202329, 0xFF1B1E24, 0xFF171A20, 0xFF292D35, 0xFF2E333C, 0xFF272B33, 0xFF303640,
            0xFFEDF0F4, 0xFFB4BFCC, 0xFFE1E8F0, 0xFFA9C3E2, 0xFF3C4D64));
    public static SkinPackage Forest { get; } = new(SkinPackage.CurrentSchemaVersion, "Forest",
        new(0xFFF0F6F1, 0xFFE6EEE7, 0xFFE0EAE2, 0xFFF9FCF9, 0xFFF7FBF7, 0xFFF1F7F1, 0xFFEAF1EB,
            0xFF253C2D, 0xFF526B59, 0xFF345D43, 0xFF376C49, 0xFFD2E7D7),
        new(0xFF172820, 0xFF14241C, 0xFF102018, 0xFF20352A, 0xFF263C30, 0xFF1C3026, 0xFF294234,
            0xFFE6F1E8, 0xFFAFCBB7, 0xFFD7EADC, 0xFF9ECFAE, 0xFF335841));
    public static SkinPackage MistBlue { get; } = new(SkinPackage.CurrentSchemaVersion, "Mist Blue",
        new(0xFFF0F5FA, 0xFFE5EDF5, 0xFFE0E9F3, 0xFFFAFCFF, 0xFFF7FAFE, 0xFFF0F5FB, 0xFFE8EFF8,
            0xFF293C51, 0xFF516A83, 0xFF365A7C, 0xFF416D98, 0xFFD7E6F5),
        new(0xFF192735, 0xFF152230, 0xFF111D2A, 0xFF223547, 0xFF283D51, 0xFF1E3042, 0xFF2B4259,
            0xFFE8F1FA, 0xFFB4CADD, 0xFFD8E7F6, 0xFFA4C9EE, 0xFF355572));

    public static IReadOnlyList<SkinPreset> All { get; } = new ReadOnlyCollection<SkinPreset>(new[]
    {
        new SkinPreset("warmWhite", "Warm White", WarmWhite),
        new SkinPreset("graphite", "Graphite", Graphite),
        new SkinPreset("forest", "Forest", Forest),
        new SkinPreset("mistBlue", "Mist Blue", MistBlue),
    });
}
