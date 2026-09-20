using System.IO;

using FilesMate.Core.Entries;

namespace FilesMate.Core.Icons;

/// <summary>
/// Cache identity for a shell icon. Directories share one key; ordinary files key by extension;
/// executables and shortcuts key by path.
/// </summary>
public readonly record struct IconKey(string Identity, int PixelSize)
{
    private static readonly HashSet<string> PathUniqueExtensions =
    [
        ".exe",
        ".dll",
        ".ico",
        ".lnk",
        ".scr",
        ".cpl",
        ".msi",
        ".appx",
        ".msix",
    ];

    public string CacheId => Identity + ":" + PixelSize.ToString();

    public static IconKey From(in FileEntryCore entry, string? fullPath, int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        if (entry.Kind == EntryKind.Directory)
        {
            return new("dir", pixelSize);
        }

        var extension = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (PathUniqueExtensions.Contains(extension) && !string.IsNullOrEmpty(fullPath))
        {
            return new("p:" + fullPath.ToLowerInvariant(), pixelSize);
        }

        return new("e:" + (extension.Length == 0 ? "." : extension), pixelSize);
    }

    public static IconKey ForPath(string path, bool directory, int pixelSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        if (directory)
        {
            return new("dir:" + path.TrimEnd('\\').ToLowerInvariant(), pixelSize);
        }

        var name = Path.GetFileName(path.TrimEnd('\\'));
        if (string.IsNullOrEmpty(name))
        {
            name = path;
        }

        return From(
            new FileEntryCore(
                Id: 0,
                Name: name,
                Size: 1,
                ModifiedUtcTicks: 1,
                CreatedUtcTicks: 1,
                Attributes: FileAttributes.Normal,
                Kind: EntryKind.File),
            path,
            pixelSize);
    }
}
