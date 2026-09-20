using System.Xml.Linq;

namespace FilesMate.App.Tests.DesignSystem;

public sealed class BrandAssetContractTests
{
    private static readonly int[] RequiredPngSizes = [24, 32, 44, 48, 64, 150, 256];
    private static readonly int[] RequiredIcoSizes = [16, 24, 32, 48, 256];

    private static string BrandingRoot => Path.Combine(ThemeXaml.AppRoot, "Assets", "Branding");

    [Fact]
    public void Vector_source_is_an_original_warm_yellow_layered_folder_mark()
    {
        var svgPath = Path.Combine(BrandingRoot, "FilesMate.svg");
        Assert.True(File.Exists(svgPath), $"Missing vector source: {svgPath}");

        var svg = File.ReadAllText(svgPath);
        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("viewBox=\"0 0 256 256\"", svg, StringComparison.Ordinal);
        Assert.Contains("folder", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mate", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("layered", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#FFD166", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#F4A525", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#FFF7DE", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0F766E", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#14B8A6", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("files-community", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0078D4", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0078d4", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_png_sizes_exist_with_matching_dimensions()
    {
        foreach (var size in RequiredPngSizes)
        {
            var path = Path.Combine(BrandingRoot, $"FilesMate-{size}.png");
            Assert.True(File.Exists(path), $"Missing PNG: {path}");
            var image = BrandImage.DecodePng(File.ReadAllBytes(path));
            Assert.Equal(size, image.Width);
            Assert.Equal(size, image.Height);
        }
    }

    [Fact]
    public void Ico_contains_the_required_windows_sizes()
    {
        var path = Path.Combine(BrandingRoot, "FilesMate.ico");
        Assert.True(File.Exists(path), $"Missing ICO: {path}");

        var sizes = BrandImage.ReadIcoSizes(File.ReadAllBytes(path));
        foreach (var size in RequiredIcoSizes)
        {
            Assert.Contains(size, sizes);
        }
    }

    [Fact]
    public void Glyph_assets_are_not_shrunk_by_transparent_padding()
    {
        foreach (var size in RequiredPngSizes)
        {
            AssertGlyphFillsCanvas(Path.Combine(BrandingRoot, $"FilesMate-{size}.png"));
        }

        foreach (var frame in BrandImage.ReadIcoFrames(File.ReadAllBytes(Path.Combine(BrandingRoot, "FilesMate.ico"))))
        {
            AssertGlyphFillsCanvas($"FilesMate.ico {frame.Width}x{frame.Height}", frame);
        }

        AssertGlyphFillsCanvas(Path.Combine(BrandingRoot, "Square44x44Logo.scale-200.png"));
        AssertGlyphFillsCanvas(Path.Combine(BrandingRoot, "Square44x44Logo.targetsize-24_altform-unplated.png"));
        AssertGlyphFillsCanvas(Path.Combine(BrandingRoot, "Square44x44Logo.targetsize-48_altform-lightunplated.png"));
    }

    [Fact]
    public void Packaged_and_unpackaged_icon_paths_resolve_to_branding_files()
    {
        var manifest = XDocument.Load(Path.Combine(ThemeXaml.AppRoot, "Package.appxmanifest"));
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

        var logo = (string?)manifest.Root?.Element(foundation + "Properties")?.Element(foundation + "Logo");
        Assert.Equal(@"Assets\Branding\StoreLogo.png", logo);
        AssertPackagedAssetExists(logo);

        var visual = manifest.Root?
            .Element(foundation + "Applications")?
            .Element(foundation + "Application")?
            .Element(uap + "VisualElements");
        Assert.NotNull(visual);

        AssertPackagedAssetExists((string?)visual!.Attribute("Square150x150Logo"));
        AssertPackagedAssetExists((string?)visual.Attribute("Square44x44Logo"));
        AssertPackagedAssetExists((string?)visual.Element(uap + "DefaultTile")?.Attribute("Wide310x150Logo"));
        AssertPackagedAssetExists((string?)visual.Element(uap + "SplashScreen")?.Attribute("Image"));

        AssertImageSize(Path.Combine(BrandingRoot, "StoreLogo.png"), 50, 50);
        AssertImageSize(Path.Combine(BrandingRoot, "Square44x44Logo.scale-200.png"), 88, 88);
        AssertImageSize(Path.Combine(BrandingRoot, "Square44x44Logo.targetsize-24_altform-unplated.png"), 24, 24);
        AssertImageSize(Path.Combine(BrandingRoot, "Square44x44Logo.targetsize-48_altform-lightunplated.png"), 48, 48);
        AssertImageSize(Path.Combine(BrandingRoot, "Square150x150Logo.scale-200.png"), 300, 300);
        AssertImageSize(Path.Combine(BrandingRoot, "Wide310x150Logo.scale-200.png"), 620, 300);
        AssertImageSize(Path.Combine(BrandingRoot, "SplashScreen.scale-200.png"), 1240, 600);
        AssertImageSize(Path.Combine(BrandingRoot, "LockScreenLogo.scale-200.png"), 48, 48);
    }

    [Fact]
    public void Project_embeds_the_branding_icon_for_unpackaged_windows()
    {
        var csproj = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "FilesMate.App.csproj"));
        Assert.Contains("<ApplicationIcon>Assets\\Branding\\FilesMate.ico</ApplicationIcon>", csproj, StringComparison.Ordinal);
        Assert.Contains("Assets\\Branding\\", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\AppIcon.ico", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets\\StoreLogo.png", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_chrome_points_at_the_branding_icon()
    {
        var xaml = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains(
            "UriSource=\"ms-appx:///Assets/Branding/FilesMate.svg\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("RasterizePixelWidth=\"20\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RasterizeBrandIcon", code, StringComparison.Ordinal);
        Assert.Contains("ShellIconBinder.RasterizePixelSize", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetIcon(\"Assets/Branding/FilesMate.ico\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_manifest_keeps_per_monitor_v2_for_crisp_icons()
    {
        var manifest = File.ReadAllText(Path.Combine(ThemeXaml.AppRoot, "app.manifest"));
        Assert.Contains("PerMonitorV2", manifest, StringComparison.Ordinal);
        Assert.Contains("true/pm", manifest, StringComparison.Ordinal);
    }

    private static void AssertPackagedAssetExists(string? manifestPath)
    {
        Assert.False(string.IsNullOrWhiteSpace(manifestPath), "Packaged visual asset path is missing.");
        Assert.StartsWith(@"Assets\Branding\", manifestPath, StringComparison.Ordinal);

        var absolute = Path.Combine(ThemeXaml.AppRoot, manifestPath.Replace('\\', Path.DirectorySeparatorChar));
        if (File.Exists(absolute))
        {
            return;
        }

        var directory = Path.GetDirectoryName(absolute) ?? BrandingRoot;
        var stem = Path.GetFileNameWithoutExtension(absolute);
        var extension = Path.GetExtension(absolute);
        var qualified = Directory.Exists(directory)
            ? Directory.GetFiles(directory, stem + ".*" + extension)
            : [];
        Assert.True(
            qualified.Length > 0,
            $"Missing packaged asset '{manifestPath}' (looked for {absolute} and '{stem}.*{extension}').");
    }

    private static void AssertImageSize(string path, int width, int height)
    {
        Assert.True(File.Exists(path), $"Missing image: {path}");
        var image = BrandImage.DecodePng(File.ReadAllBytes(path));
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    private static void AssertGlyphFillsCanvas(string path)
    {
        Assert.True(File.Exists(path), $"Missing glyph asset: {path}");
        AssertGlyphFillsCanvas(Path.GetFileName(path), BrandImage.DecodePng(File.ReadAllBytes(path)));
    }

    private static void AssertGlyphFillsCanvas(string name, BrandImage image)
    {
        var bounds = image.OpaqueBounds(alphaThreshold: 16);
        Assert.True(bounds.HasValue, $"{name} is fully transparent.");

        var box = bounds.Value;
        var width = image.Width;
        var height = image.Height;
        var padLeft = box.Left / (double)width;
        var padTop = box.Top / (double)height;
        var padRight = (width - 1 - box.Right) / (double)width;
        var padBottom = (height - 1 - box.Bottom) / (double)height;
        var coverX = (box.Right - box.Left + 1) / (double)width;
        var coverY = (box.Bottom - box.Top + 1) / (double)height;

        Assert.True(padLeft <= 0.18, $"{name} left padding {padLeft:0.000} is too large.");
        Assert.True(padTop <= 0.18, $"{name} top padding {padTop:0.000} is too large.");
        Assert.True(padRight <= 0.18, $"{name} right padding {padRight:0.000} is too large.");
        Assert.True(padBottom <= 0.18, $"{name} bottom padding {padBottom:0.000} is too large.");
        Assert.True(coverX >= 0.64, $"{name} glyph width {coverX:0.000} is too small.");
        Assert.True(coverY >= 0.64, $"{name} glyph height {coverY:0.000} is too small.");
    }
}

internal readonly record struct BrandImage(int Width, int Height, byte[] Rgba)
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal readonly record struct Bounds(int Left, int Top, int Right, int Bottom);

    internal Bounds? OpaqueBounds(byte alphaThreshold)
    {
        var left = Width;
        var top = Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (Rgba[((y * Width) + x) * 4 + 3] <= alphaThreshold)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < 0 ? null : new Bounds(left, top, right, bottom);
    }

    internal static BrandImage DecodePng(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("PNG signature is missing.");
        }

        var width = 0;
        var height = 0;
        using var idat = new MemoryStream();
        var offset = 8;
        while (offset + 8 <= data.Length)
        {
            var length = ReadBigEndianInt(data, offset);
            var type = EncodingType(data, offset + 4);
            var payloadStart = offset + 8;
            var payloadEnd = payloadStart + length;
            if (payloadEnd + 4 > data.Length)
            {
                throw new InvalidDataException($"PNG chunk '{type}' is truncated.");
            }

            if (type == "IHDR")
            {
                width = ReadBigEndianInt(data, payloadStart);
                height = ReadBigEndianInt(data, payloadStart + 4);
                if (data[payloadStart + 8] != 8 || data[payloadStart + 9] != 6)
                {
                    throw new InvalidDataException("Brand PNGs must be 8-bit RGBA.");
                }
            }
            else if (type == "IDAT")
            {
                idat.Write(data, payloadStart, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            offset = payloadEnd + 4;
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PNG IHDR is missing.");
        }

        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        return new BrandImage(width, height, Unfilter(inflated.ToArray(), width, height));
    }

    internal static int[] ReadIcoSizes(byte[] data)
        => ReadIcoFrames(data).Select(frame => frame.Width).ToArray();

    internal static BrandImage[] ReadIcoFrames(byte[] data)
    {
        if (data.Length < 6)
        {
            throw new InvalidDataException("ICO is truncated.");
        }

        var count = BitConverter.ToUInt16(data, 4);
        var frames = new BrandImage[count];
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);
            var bytes = BitConverter.ToInt32(data, entry + 8);
            var imageOffset = BitConverter.ToInt32(data, entry + 12);
            var slice = data.AsSpan(imageOffset, bytes).ToArray();
            frames[i] = DecodePng(slice);
        }

        return frames;
    }

    private static byte[] Unfilter(byte[] filtered, int width, int height)
    {
        const int bytesPerPixel = 4;
        var stride = width * bytesPerPixel;
        var pixels = new byte[stride * height];
        var source = 0;
        byte[]? previous = null;
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[source++];
            var row = new byte[stride];
            for (var i = 0; i < stride; i++)
            {
                var raw = filtered[source++];
                var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : (byte)0;
                var up = previous is null ? (byte)0 : previous[i];
                var upLeft = previous is null || i < bytesPerPixel ? (byte)0 : previous[i - bytesPerPixel];
                row[i] = filter switch
                {
                    0 => raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    3 => (byte)(raw + ((left + up) / 2)),
                    4 => (byte)(raw + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException($"Unsupported PNG filter '{filter}'."),
                };
            }

            row.CopyTo(pixels, y * stride);
            previous = row;
        }

        return pixels;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static int ReadBigEndianInt(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static string EncodingType(byte[] data, int offset)
        => System.Text.Encoding.ASCII.GetString(data, offset, 4);
}
