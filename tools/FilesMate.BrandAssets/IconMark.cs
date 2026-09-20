using System.Globalization;
using System.Text;

namespace FilesMate.BrandAssets;

internal readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    internal static Rgba Transparent { get; } = new(0, 0, 0, 0);

    internal static Rgba FromHex(string hex, byte alpha = 255)
    {
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new(
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF),
            alpha);
    }

    internal Rgba WithAlpha(byte alpha) => this with { A = alpha };

    internal static Rgba Over(Rgba destination, Rgba source)
    {
        if (source.A == 0)
        {
            return destination;
        }

        if (source.A == 255)
        {
            return source;
        }

        if (destination.A == 0)
        {
            return source;
        }

        var sa = source.A / 255f;
        var da = destination.A / 255f;
        var outA = sa + (da * (1f - sa));
        if (outA <= 0f)
        {
            return Transparent;
        }

        var r = ((source.R * sa) + (destination.R * da * (1f - sa))) / outA;
        var g = ((source.G * sa) + (destination.G * da * (1f - sa))) / outA;
        var b = ((source.B * sa) + (destination.B * da * (1f - sa))) / outA;
        return new(ToByte(r), ToByte(g), ToByte(b), ToByte(outA * 255f));
    }

    internal static Rgba Scale(Rgba color, float alpha)
        => color with { A = ToByte(color.A * alpha) };

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}

internal readonly record struct MarkRect(float X, float Y, float Width, float Height, float Radius, Rgba Color);

internal static class IconMark
{
    internal const string FolderHex = "#F4A525";
    internal const string TabHex = "#FFD166";
    internal const string PageHex = "#FFF7DE";
    internal const string PageBackHex = "#FFE6A3";
    internal const string LineHex = "#B96E13";
    internal const string PlateHex = "#6E430D";

    private static readonly Rgba Folder = Rgba.FromHex(FolderHex);
    private static readonly Rgba Tab = Rgba.FromHex(TabHex);
    private static readonly Rgba PageBack = Rgba.FromHex(PageBackHex);
    private static readonly Rgba PageFront = Rgba.FromHex(PageHex);
    private static readonly Rgba Plate = Rgba.FromHex(PlateHex);

    private static readonly MarkRect[] ForegroundShapes =
    [
        new(0.48f, 0.24f, 0.32f, 0.45f, 0.050f, PageBack),
        new(0.35f, 0.28f, 0.34f, 0.45f, 0.052f, PageFront),
        new(0.09f, 0.42f, 0.82f, 0.47f, 0.115f, Folder),
        new(0.57f, 0.60f, 0.09f, 0.09f, 0.027f, PageFront.WithAlpha(230)),
        new(0.70f, 0.60f, 0.09f, 0.09f, 0.027f, PageFront.WithAlpha(176)),
        new(0.57f, 0.73f, 0.22f, 0.065f, 0.025f, PageFront.WithAlpha(176)),
    ];

    internal static byte[] RenderGlyph(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var color = Sample((x + 0.5f) / size, (y + 0.5f) / size, size);
                Write(pixels, x, y, size, color);
            }
        }

        return pixels;
    }

    internal static byte[] RenderPlated(int size)
    {
        var glyph = RenderGlyph(size);
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size;
                var v = (y + 0.5f) / size;
                var plateCoverage = Coverage(RoundedRectSdf(u, v, 0.02f, 0.02f, 0.96f, 0.96f, 0.20f), size);
                var plate = Rgba.Scale(Plate, plateCoverage);
                var glyphColor = Read(glyph, x, y, size);
                Write(pixels, x, y, size, Rgba.Over(plate, glyphColor));
            }
        }

        return pixels;
    }

    internal static byte[] RenderBanner(int width, int height, int glyphSize)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Plate.R;
            pixels[i + 1] = Plate.G;
            pixels[i + 2] = Plate.B;
            pixels[i + 3] = 255;
        }

        var glyph = RenderGlyph(glyphSize);
        var originX = (width - glyphSize) / 2;
        var originY = (height - glyphSize) / 2;
        for (var y = 0; y < glyphSize; y++)
        {
            var dy = originY + y;
            if ((uint)dy >= (uint)height)
            {
                continue;
            }

            for (var x = 0; x < glyphSize; x++)
            {
                var dx = originX + x;
                if ((uint)dx >= (uint)width)
                {
                    continue;
                }

                var destination = Read(pixels, dx, dy, width);
                var source = Read(glyph, x, y, glyphSize);
                Write(pixels, dx, dy, width, Rgba.Over(destination, source));
            }
        }

        return pixels;
    }

    internal static string ToSvg()
    {
        var svg = new StringBuilder();
        svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" role=\"img\" aria-label=\"FilesMate\">");
        svg.AppendLine("  <!-- FilesMate warm yellow layered folder + mate pages. Original mark, not the Files app icon. -->");
        svg.AppendLine("  <path id=\"back\" d=\"M52.48 33.28h46.08c9.19 0 16.64 7.45 16.64 16.64v.96c0 4.42 3.58 8 8 8h80.32c16.26 0 29.44 13.18 29.44 29.44V198.4c0 16.26-13.18 29.44-29.44 29.44H52.48c-16.26 0-29.44-13.18-29.44-29.44V62.72c0-16.26 13.18-29.44 29.44-29.44Z\" fill=\"#FFD166\" />");
        foreach (var shape in ForegroundShapes)
        {
            svg.Append("  <rect")
                .Append(" x=\"").Append(Svg(shape.X * 256)).Append('"')
                .Append(" y=\"").Append(Svg(shape.Y * 256)).Append('"')
                .Append(" width=\"").Append(Svg(shape.Width * 256)).Append('"')
                .Append(" height=\"").Append(Svg(shape.Height * 256)).Append('"')
                .Append(" rx=\"").Append(Svg(shape.Radius * 256)).Append('"')
                .Append(" fill=\"").Append(ToHex(shape.Color)).Append('"');
            if (shape.Color.A < 255)
            {
                svg.Append(" fill-opacity=\"").Append(Svg(shape.Color.A / 255f)).Append('"');
            }

            svg.AppendLine(" />");
        }

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static Rgba Sample(float u, float v, int size)
    {
        var backCoverage = Coverage(FolderBackSdf(u, v), size);
        var color = Rgba.Scale(Tab, backCoverage);
        foreach (var shape in ForegroundShapes)
        {
            var coverage = Coverage(
                RoundedRectSdf(u, v, shape.X, shape.Y, shape.Width, shape.Height, shape.Radius),
                size);
            if (coverage <= 0f)
            {
                continue;
            }

            color = Rgba.Over(color, Rgba.Scale(shape.Color, coverage));
        }

        return color;
    }

    private static float FolderBackSdf(float u, float v)
    {
        var tab = RoundedRectSdf(u, v, 0.09f, 0.13f, 0.36f, 0.22f, 0.065f);
        var body = RoundedRectSdf(u, v, 0.09f, 0.23f, 0.82f, 0.66f, 0.115f);
        var bridge = RoundedRectSdf(u, v, 0.09f, 0.20f, 0.43f, 0.22f, 0f);
        return MathF.Min(tab, MathF.Min(body, bridge));
    }

    private static float RoundedRectSdf(float u, float v, float x, float y, float width, float height, float radius)
    {
        var radiusMax = MathF.Min(width, height) * 0.5f;
        var r = Math.Clamp(radius, 0f, radiusMax);
        var px = u - (x + (width * 0.5f));
        var py = v - (y + (height * 0.5f));
        var qx = MathF.Abs(px) - ((width * 0.5f) - r);
        var qy = MathF.Abs(py) - ((height * 0.5f) - r);
        var outside = MathF.Sqrt(Square(MathF.Max(qx, 0f)) + Square(MathF.Max(qy, 0f)));
        return outside + MathF.Min(MathF.Max(qx, qy), 0f) - r;
    }

    private static float Coverage(float sdfUv, int size)
        => Math.Clamp(0.5f - (sdfUv * size), 0f, 1f);

    private static float Square(float value) => value * value;

    private static void Write(byte[] pixels, int x, int y, int stride, Rgba color)
    {
        var index = ((y * stride) + x) * 4;
        pixels[index] = color.R;
        pixels[index + 1] = color.G;
        pixels[index + 2] = color.B;
        pixels[index + 3] = color.A;
    }

    private static Rgba Read(byte[] pixels, int x, int y, int stride)
    {
        var index = ((y * stride) + x) * 4;
        return new(pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    private static string Svg(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string ToHex(Rgba color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
