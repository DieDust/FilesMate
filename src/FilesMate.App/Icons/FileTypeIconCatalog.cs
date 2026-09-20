using System.IO;

using FilesMate.Core.Entries;

namespace FilesMate.App.Icons;

internal enum FileIconKind
{
    Folder,
    Link,
    Archive,
    Document,
    Pdf,
    Word,
    Spreadsheet,
    Presentation,
    Text,
    Markdown,
    Json,
    Xml,
    Html,
    Csv,
    Log,
    Ebook,
    Code,
    JavaScript,
    TypeScript,
    CSharp,
    Cpp,
    Python,
    Rust,
    Go,
    Java,
    Yaml,
    Toml,
    Zip,
    SevenZip,
    Rar,
    Tar,
    Image,
    Audio,
    Video,
    Database,
    Configuration,
    System,
    Generic,
}

/// <summary>
/// Deterministic, project-owned format icon routing. Unknown and executable
/// formats intentionally remain on the Windows shell icon path.
/// </summary>
internal static class FileTypeIconCatalog
{
    private static readonly HashSet<string> ArchiveExtensions =
    [
        ".gz", ".bz2", ".xz", ".zst", ".iso", ".cab", ".lz", ".lz4", ".lzh",
        ".zipx", ".gzip", ".tgz", ".bzip2", ".tbz2", ".txz", ".lzma", ".tzst",
        ".wim", ".swm", ".esd", ".ar", ".cpio", ".rpm", ".deb", ".dmg",
    ];

    private static readonly HashSet<string> CompactMateExtractExtensions = CreateExtractExtensions();

    private static readonly HashSet<string> LinkExtensions =
    [
        ".lnk", ".url", ".website",
    ];

    private static readonly HashSet<string> ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic",
    ];

    private static readonly HashSet<string> AudioExtensions =
    [
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".wma", ".ogg", ".opus", ".aiff",
    ];

    private static readonly HashSet<string> VideoExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpeg", ".mpg",
    ];

    private static readonly HashSet<string> DocumentExtensions =
    [
        ".rtf", ".odt", ".pages", ".tex", ".nfo", ".readme",
    ];

    private static readonly HashSet<string> DatabaseExtensions =
    [
        ".db", ".db3", ".sqlite", ".sqlite3", ".mdb", ".accdb",
    ];

    private static readonly HashSet<string> ConfigurationExtensions =
    [
        ".vdf", ".ini", ".cfg", ".conf", ".config", ".properties", ".reg",
    ];

    private static readonly HashSet<string> SystemExtensions =
    [
        ".dll", ".sys", ".drv", ".ocx",
    ];

    private static readonly HashSet<string> ShellIdentityExtensions =
    [
        ".exe", ".msi", ".appx", ".msix", ".scr", ".cpl",
    ];

    public static FileIconKind? Classify(in FileEntryCore entry)
    {
        return ClassifyName(entry.Name, entry.Kind == EntryKind.Directory);
    }

    public static FileIconKind? ClassifyPath(string? path, bool directory)
    {
        return ClassifyName(path, directory);
    }

    private static FileIconKind? ClassifyName(string? name, bool directory)
    {
        if (directory)
        {
            return FileIconKind.Folder;
        }

        var extension = Path.GetExtension(name ?? string.Empty).ToLowerInvariant();
        if (LinkExtensions.Contains(extension))
        {
            return FileIconKind.Link;
        }

        if (ShellIdentityExtensions.Contains(extension))
        {
            return null;
        }

        var specificKind = extension switch
        {
            ".pdf" => FileIconKind.Pdf,
            ".doc" or ".docx" or ".odt" or ".rtf" => FileIconKind.Word,
            ".xls" or ".xlsx" or ".ods" => FileIconKind.Spreadsheet,
            ".ppt" or ".pptx" or ".odp" => FileIconKind.Presentation,
            ".txt" => FileIconKind.Text,
            ".md" or ".markdown" => FileIconKind.Markdown,
            ".json" or ".jsonc" => FileIconKind.Json,
            ".xml" or ".xaml" => FileIconKind.Xml,
            ".html" or ".htm" or ".xhtml" => FileIconKind.Html,
            ".csv" or ".tsv" => FileIconKind.Csv,
            ".log" => FileIconKind.Log,
            ".epub" or ".mobi" or ".azw" or ".azw3" => FileIconKind.Ebook,
            ".js" or ".jsx" or ".mjs" or ".cjs" => FileIconKind.JavaScript,
            ".ts" or ".tsx" or ".mts" or ".cts" => FileIconKind.TypeScript,
            ".cs" => FileIconKind.CSharp,
            ".c" or ".cc" or ".cpp" or ".cxx" or ".h" or ".hh" or ".hpp" or ".hxx" => FileIconKind.Cpp,
            ".py" or ".pyw" or ".pyi" => FileIconKind.Python,
            ".rs" => FileIconKind.Rust,
            ".go" => FileIconKind.Go,
            ".java" or ".kt" or ".kts" => FileIconKind.Java,
            ".yaml" or ".yml" => FileIconKind.Yaml,
            ".toml" => FileIconKind.Toml,
            ".css" or ".scss" or ".sass" or ".less" or ".sh" or ".bash" or ".ps1" or ".bat" or ".cmd" or ".sql" or ".lua" or ".php" or ".rb" or ".swift" or ".dart" => FileIconKind.Code,
            ".zip" => FileIconKind.Zip,
            ".7z" => FileIconKind.SevenZip,
            ".rar" => FileIconKind.Rar,
            ".tar" or ".tgz" or ".tbz" or ".tbz2" or ".txz" => FileIconKind.Tar,
            _ => (FileIconKind?)null,
        };
        if (specificKind is not null)
        {
            return specificKind;
        }

        if (ArchiveExtensions.Contains(extension))
        {
            return FileIconKind.Archive;
        }

        if (ImageExtensions.Contains(extension))
        {
            return FileIconKind.Image;
        }

        if (AudioExtensions.Contains(extension))
        {
            return FileIconKind.Audio;
        }

        if (VideoExtensions.Contains(extension))
        {
            return FileIconKind.Video;
        }

        if (DatabaseExtensions.Contains(extension))
        {
            return FileIconKind.Database;
        }

        if (ConfigurationExtensions.Contains(extension))
        {
            return FileIconKind.Configuration;
        }

        if (SystemExtensions.Contains(extension))
        {
            return FileIconKind.System;
        }

        return DocumentExtensions.Contains(extension)
            ? FileIconKind.Document
            : FileIconKind.Generic;
    }

    public static bool IsImage(in FileEntryCore entry) =>
        Classify(entry) == FileIconKind.Image;

    public static bool IsArchivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var kind = ClassifyPath(path, directory: false);
        if (kind is FileIconKind.Archive
            or FileIconKind.Zip
            or FileIconKind.SevenZip
            or FileIconKind.Rar
            or FileIconKind.Tar)
        {
            return true;
        }

        return CompactMateExtractExtensions.Contains(Path.GetExtension(path));
    }

    private static HashSet<string> CreateExtractExtensions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".7z", ".zip", ".zipx", ".rar", ".tar", ".gz", ".gzip", ".tgz",
            ".bz2", ".bzip2", ".tbz", ".tbz2", ".xz", ".txz", ".lz", ".lzma",
            ".zst", ".tzst", ".cab", ".iso", ".wim", ".swm", ".esd",
            ".ar", ".cpio", ".rpm", ".deb", ".dmg", ".001",
        };
        for (var i = 1; i <= 40; i++)
        {
            set.Add("." + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
        }

        for (var i = 0; i <= 39; i++)
        {
            set.Add(".r" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
            if (i >= 1)
            {
                set.Add(".z" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return set;
    }

    /// <summary>
    /// Formats whose icon is meaningful per individual file should use the
    /// Windows Shell result before FilesMate's generic format artwork.
    /// </summary>
    public static bool PrefersShell(FileIconKind? kind) => kind == FileIconKind.Link;

    public static bool IsImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public static bool IsThumbnailPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension);
    }

    public static string AssetUri(FileIconKind kind) =>
        $"ms-appx:///Assets/FileIcons/{kind switch
        {
            FileIconKind.Folder => "folder",
            FileIconKind.Link => "link",
            FileIconKind.Archive => "archive",
            FileIconKind.Document => "document",
            FileIconKind.Pdf => "pdf",
            FileIconKind.Word => "word",
            FileIconKind.Spreadsheet => "spreadsheet",
            FileIconKind.Presentation => "presentation",
            FileIconKind.Text => "text",
            FileIconKind.Markdown => "markdown",
            FileIconKind.Json => "json",
            FileIconKind.Xml => "xml",
            FileIconKind.Html => "html",
            FileIconKind.Csv => "csv",
            FileIconKind.Log => "log",
            FileIconKind.Ebook => "ebook",
            FileIconKind.Code => "code",
            FileIconKind.JavaScript => "javascript",
            FileIconKind.TypeScript => "typescript",
            FileIconKind.CSharp => "csharp",
            FileIconKind.Cpp => "cpp",
            FileIconKind.Python => "python",
            FileIconKind.Rust => "rust",
            FileIconKind.Go => "go",
            FileIconKind.Java => "java",
            FileIconKind.Yaml => "yaml",
            FileIconKind.Toml => "toml",
            FileIconKind.Zip => "zip",
            FileIconKind.SevenZip => "sevenzip",
            FileIconKind.Rar => "rar",
            FileIconKind.Tar => "tar",
            FileIconKind.Image => "image",
            FileIconKind.Audio => "audio",
            FileIconKind.Video => "video",
            FileIconKind.Database => "database",
            FileIconKind.Configuration => "configuration",
            FileIconKind.System => "system",
            FileIconKind.Generic => "generic",
            _ => "document",
        }}.svg";
}
