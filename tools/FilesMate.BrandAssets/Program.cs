using FilesMate.BrandAssets;

internal static class Program
{
    private static readonly int[] PngSizes = [24, 32, 44, 48, 64, 150, 256];
    private static readonly int[] IcoSizes = [16, 24, 32, 48, 256];

    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(FindRepoRoot(), "src", "FilesMate.App", "Assets", "Branding");
        var fileIconOutput = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.Combine(Path.GetDirectoryName(output)!, "FileIcons");

        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "FilesMate.svg"), IconMark.ToSvg());

        foreach (var size in PngSizes)
        {
            WritePng(output, $"FilesMate-{size}.png", size, size, IconMark.RenderGlyph(size));
        }

        var icoFrames = IcoSizes
            .Select(size => (Size: size, Png: ImageFormats.WritePng(size, size, IconMark.RenderGlyph(size))))
            .ToArray();
        File.WriteAllBytes(Path.Combine(output, "FilesMate.ico"), ImageFormats.WriteIco(icoFrames));

        WritePng(output, "StoreLogo.png", 50, 50, IconMark.RenderPlated(50));
        WritePng(output, "Square44x44Logo.scale-200.png", 88, 88, IconMark.RenderGlyph(88));
        WritePng(output, "Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24, IconMark.RenderGlyph(24));
        WritePng(output, "Square44x44Logo.targetsize-48_altform-lightunplated.png", 48, 48, IconMark.RenderGlyph(48));
        WritePng(output, "Square150x150Logo.scale-200.png", 300, 300, IconMark.RenderPlated(300));
        WritePng(output, "Wide310x150Logo.scale-200.png", 620, 300, IconMark.RenderBanner(620, 300, 220));
        WritePng(output, "SplashScreen.scale-200.png", 1240, 600, IconMark.RenderBanner(1240, 600, 256));
        WritePng(output, "LockScreenLogo.scale-200.png", 48, 48, IconMark.RenderGlyph(48));
        FileIconAssets.WriteAll(fileIconOutput);

        Console.WriteLine($"Wrote FilesMate brand assets to {output}");
        Console.WriteLine($"Wrote FilesMate semantic file icons to {fileIconOutput}");
        return 0;
    }

    private static void WritePng(string output, string fileName, int width, int height, byte[] rgba)
        => File.WriteAllBytes(Path.Combine(output, fileName), ImageFormats.WritePng(width, height, rgba));

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FilesMate.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "scripts")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }
}
