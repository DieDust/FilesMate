namespace FilesMate.App.Models;

public enum AccentKind
{
    Default,
    Gold,
    Orange,
    BrickRed,
    Red,
    Rose,
    Blue,
    Iris,
    Violet,
    CoolBlue,
    Seafoam,
    Mint,
    Gray,
    Green,
    Overcast,
    Storm,
    BlueGray,
    Custom,
}

public readonly record struct AccentSwatch(AccentKind Kind, string NameKey, uint Argb);

public static class AccentPalette
{
    public static readonly AccentSwatch[] Presets =
    [
        new(AccentKind.Default, "Accent_Default", 0xFF007AFF),
        new(AccentKind.Gold, "Accent_Gold", 0xFFFFB900),
        new(AccentKind.Orange, "Accent_Orange", 0xFFFF8C00),
        new(AccentKind.BrickRed, "Accent_BrickRed", 0xFFE81123),
        new(AccentKind.Red, "Accent_Red", 0xFFE3008C),
        new(AccentKind.Rose, "Accent_Rose", 0xFFC239B3),
        new(AccentKind.Blue, "Accent_Blue", 0xFF0078D4),
        new(AccentKind.Iris, "Accent_Iris", 0xFF8764B8),
        new(AccentKind.Violet, "Accent_Violet", 0xFFB146C2),
        new(AccentKind.CoolBlue, "Accent_CoolBlue", 0xFF00B7C3),
        new(AccentKind.Seafoam, "Accent_Seafoam", 0xFF00B294),
        new(AccentKind.Mint, "Accent_Mint", 0xFF00CC6A),
        new(AccentKind.Gray, "Accent_Gray", 0xFF7A7574),
        new(AccentKind.Green, "Accent_Green", 0xFF10893E),
        new(AccentKind.Overcast, "Accent_Overcast", 0xFF6B6B6B),
        new(AccentKind.Storm, "Accent_Storm", 0xFF4C4A48),
        new(AccentKind.BlueGray, "Accent_BlueGray", 0xFF5D5A58),
    ];

    public static uint Resolve(AccentKind kind, string? custom, bool dark = false)
    {
        if (kind == AccentKind.Custom && TryParse(custom, out var customArgb))
        {
            return customArgb;
        }

        if (kind == AccentKind.Default)
        {
            return dark ? 0xFF0A84FF : 0xFF007AFF;
        }

        foreach (var preset in Presets)
        {
            if (preset.Kind == kind)
            {
                return preset.Argb;
            }
        }

        return Presets[0].Argb;
    }

    public static bool TryParse(string? value, out uint argb)
    {
        argb = Presets[0].Argb;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            argb = 0xFF000000 | rgb;
            return true;
        }

        if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out argb))
        {
            return true;
        }

        return false;
    }

    public static string ToHex(uint argb) => $"#{argb:X8}";
}
