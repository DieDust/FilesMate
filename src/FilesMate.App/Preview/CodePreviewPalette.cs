namespace FilesMate.App.Preview;

/// <summary>Functional syntax colors resolved against an opaque reading surface.</summary>
internal readonly record struct CodePreviewPalette(uint String, uint Number, uint Keyword, uint Comment, uint Error)
{
    private const double MinimumContrast = 4.5;

    // Theme names do not describe custom skin pixels. Choose the semantic seed
    // colors from the actual background, then adjust only insufficient contrast.
    internal static CodePreviewPalette Resolve(uint background)
    {
        var luminance = Luminance(background);
        var blackContrast = (luminance + .05) / .05;
        var whiteContrast = 1.05 / (luminance + .05);
        var dark = whiteContrast > blackContrast;
        var target = dark ? 0xFFFFFFFFu : 0xFF000000u;
        uint Readable(uint color)
        {
            if (Contrast(color, luminance) >= MinimumContrast) return color;
            var low = 0d; var high = 1d;
            var result = target;
            // Keep the closest passing rounded RGB value along the same hue's
            // path to black/white. Either endpoint always provides at least 4.5:1.
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var amount = (low + high) / 2;
                var candidate = Mix(target, color, amount);
                if (Contrast(candidate, luminance) >= MinimumContrast)
                {
                    high = amount;
                    result = candidate;
                }
                else low = amount;
            }
            return result;
        }
        return new(Readable(dark ? 0xFFCE9178 : 0xFF9B3B08),
            Readable(dark ? 0xFFB5CEA8 : 0xFF286B37),
            Readable(dark ? 0xFF82AAFF : 0xFF075AA4),
            Readable(dark ? 0xFF8BA888 : 0xFF547548),
            Readable(dark ? 0xFFFF9898 : 0xFFB32323));
    }

    private static uint Mix(uint target, uint color, double amount)
    {
        uint Channel(int shift) => (uint)Math.Round(((target >> shift) & 255) * amount + ((color >> shift) & 255) * (1 - amount));
        return 0xFF000000 | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static double Contrast(uint color, double backgroundLuminance)
    {
        var foreground = Luminance(color);
        return (Math.Max(foreground, backgroundLuminance) + .05) / (Math.Min(foreground, backgroundLuminance) + .05);
    }

    private static double Luminance(uint color)
    {
        static double Linear(uint channel)
        {
            var value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear((color >> 16) & 255) + .7152 * Linear((color >> 8) & 255) + .0722 * Linear(color & 255);
    }
}
