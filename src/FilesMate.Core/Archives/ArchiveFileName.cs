using System.Text.RegularExpressions;

namespace FilesMate.Core.Archives;

/// <summary>Name-only archive hints for menus; extraction still validates the file contents.</summary>
public static partial class ArchiveFileName
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".zip", ".zipx", ".rar", ".tar", ".gz", ".gzip", ".tgz",
        ".bz2", ".bzip2", ".tbz", ".tbz2", ".xz", ".txz", ".lz", ".lzma",
        ".lz4", ".lzh", ".zst", ".tzst", ".cab", ".iso", ".wim", ".swm",
        ".esd", ".ar", ".cpio", ".rpm", ".deb", ".dmg",
    };

    // Strip text appended to an archive/volume suffix, never a further extension:
    // archive.7z.002删删删 is a volume; instructions.zip.txt stays a text file.
    [GeneratedRegex(@"^(.+?(?:\.part[0-9]+\.rar|\.(?:7z|zipx?)(?:\.[0-9]{3,})?|\.rar|\.[rz][0-9]{2}|\.[0-9]{3,}))[^.]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DecoratedName();

    [GeneratedRegex(@"^\.(?:[0-9]{3,}|[rz][0-9]{2})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VolumeExtension();

    public static string CanonicalName(string path)
    {
        var name = Path.GetFileName(path);
        var match = DecoratedName().Match(name);
        return match.Success ? match.Groups[1].Value : name;
    }

    public static bool IsArchive(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var extension = Path.GetExtension(CanonicalName(path));
        return Extensions.Contains(extension) || VolumeExtension().IsMatch(extension);
    }

    public static bool IsSingleZip(string path) =>
        Path.GetExtension(CanonicalName(path)).Equals(".zip", StringComparison.OrdinalIgnoreCase);
}
