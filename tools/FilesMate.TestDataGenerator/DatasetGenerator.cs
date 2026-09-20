using System.Text;
using System.Text.Json;

namespace FilesMate.TestDataGenerator;

public sealed class DatasetGenerator
{
    public const string GeneratorId = "FilesMate.TestDataGenerator";
    public const string MarkerFileName = ".filesmate-dataset-marker";
    public const int MarkerVersion = 1;

    private static readonly Dictionary<string, int> DefaultMixedWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = 20,
        [".md"] = 10,
        [".cs"] = 10,
        [".json"] = 10,
        [".xml"] = 8,
        [".log"] = 7,
        [".png"] = 10,
        [".jpg"] = 8,
        [".pdf"] = 5,
        [".zip"] = 5,
        [".dll"] = 3,
        [""] = 4,
    };

    // 1x1 transparent PNG.
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    // Minimal valid 1x1 JPEG (SOF0 / SOS / EOI).
    private static readonly byte[] TinyJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
        0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
        0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
        0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
        0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
        0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
        0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
        0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x03, 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00,
        0x7F, 0xFF, 0xD9,
    ];

    public DatasetGenerationResult Create(GenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = PathGuard.ValidateNewRoot(options.Root);
        if (options.FileCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FileCount must be >= 0.");
        }

        if (options.DirectoryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "DirectoryCount must be >= 0.");
        }

        if (options.Depth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Depth must be >= 1.");
        }

        Directory.CreateDirectory(ToExtendedPath(root));

        var markerPath = Path.Combine(root, MarkerFileName);
        WriteMarker(markerPath, options, fileCount: 0, directoryCount: 0);

        var relativeDirs = CreateDirectories(root, options);
        var relativeFiles = CreateFiles(root, relativeDirs, options);

        WriteMarker(markerPath, options, relativeFiles.Count, Math.Max(0, relativeDirs.Count - 1));

        return new DatasetGenerationResult
        {
            Root = root,
            MarkerPath = markerPath,
            FileCount = relativeFiles.Count,
            DirectoryCount = Math.Max(0, relativeDirs.Count - 1),
            RelativeFileNames = relativeFiles,
        };
    }

    public void Clean(string root, bool requireMarker = true)
    {
        var full = PathGuard.ValidateExistingRoot(root);
        var markerPath = Path.Combine(full, MarkerFileName);
        if (requireMarker && !File.Exists(ToExtendedPath(markerPath)))
        {
            throw new InvalidOperationException(
                $"Refusing to clean '{full}' because the FilesMate dataset marker was not found.");
        }

        DeleteTree(full);
    }

    private static List<string> CreateDirectories(string root, GenerationOptions options)
    {
        var relativeDirs = new List<string> { string.Empty };
        if (options.Profile == DatasetProfile.Empty || options.DirectoryCount == 0)
        {
            return relativeDirs;
        }

        var rng = new Random(options.Seed ^ 0x5EED);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { string.Empty };
        var maxDepth = options.Depth;

        for (var i = 0; i < options.DirectoryCount; i++)
        {
            var candidates = relativeDirs
                .Where(dir => GetDepth(dir) < maxDepth)
                .ToArray();
            var parent = candidates.Length == 0 ? string.Empty : candidates[rng.Next(candidates.Length)];
            string relative;
            do
            {
                relative = CombineRelative(parent, $"d{i:D5}_{rng.Next(16_384):x4}");
            }
            while (!used.Add(relative));

            var full = Path.Combine(root, relative);
            Directory.CreateDirectory(ToExtendedPath(full));
            ThrowIfReparsePoint(full);
            relativeDirs.Add(relative);
        }

        return relativeDirs;
    }

    private static IReadOnlyList<string> CreateFiles(string root, IReadOnlyList<string> relativeDirs, GenerationOptions options)
    {
        if (options.Profile == DatasetProfile.Empty || options.FileCount == 0)
        {
            return [];
        }

        var rng = new Random(options.Seed);
        var names = BuildFileNames(options, rng);
        var created = new string[names.Count];

        for (var i = 0; i < names.Count; i++)
        {
            var dir = relativeDirs[i % relativeDirs.Count];
            var relative = CombineRelative(dir, names[i]);
            var full = Path.Combine(root, relative);
            WriteFile(full, options.Profile, i, names[i]);
            created[i] = relative;
        }

        return created;
    }

    private static List<string> BuildFileNames(GenerationOptions options, Random rng)
    {
        var names = new List<string>(options.FileCount);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.Profile is DatasetProfile.Unicode or DatasetProfile.LongPaths)
        {
            foreach (var special in SpecialNames())
            {
                if (names.Count >= options.FileCount)
                {
                    break;
                }

                if (used.Add(special))
                {
                    names.Add(special);
                }
            }
        }

        IReadOnlyDictionary<string, int> weights = options.ExtensionWeights ?? DefaultMixedWeights;
        var weightKeys = weights.Keys.Where(key => weights[key] > 0).ToArray();
        if (options.Profile == DatasetProfile.Mixed && options.ExtensionWeights is not null)
        {
            foreach (var ext in weightKeys)
            {
                if (names.Count >= options.FileCount)
                {
                    break;
                }

                AddUnique(names, used, $"weighted{names.Count:D5}{NormalizeExtension(ext)}");
            }
        }

        while (names.Count < options.FileCount)
        {
            var name = options.Profile switch
            {
                DatasetProfile.Images => $"img{names.Count:D6}{(names.Count % 2 == 0 ? ".png" : ".jpg")}",
                DatasetProfile.Unicode => $"u{names.Count:D5}_{rng.Next(0xFFFF):x4}.txt",
                DatasetProfile.LongPaths => $"{new string('p', 180)}{names.Count:D5}.dat",
                _ => $"f{names.Count:D6}_{rng.Next(0xFFFFF):x5}{PickExtension(rng, weights, weightKeys)}",
            };
            AddUnique(names, used, name);
        }

        return names;
    }

    private static IEnumerable<string> SpecialNames()
    {
        yield return "空文件.txt";
        yield return "文档-日本語-한글.txt";
        yield return "😀-emoji-file.txt";
        yield return "שלום-مرحبا.txt";
        yield return "cafe\u0301.txt";
        yield return "CON_backup.txt";
        yield return "NUL-data.txt";
        yield return "AUX-copy.txt";
        yield return new string('N', 210) + ".txt";
        yield return "zero-byte.dat";
    }

    private static void AddUnique(List<string> names, HashSet<string> used, string name)
    {
        var candidate = name;
        var n = 0;
        while (!used.Add(candidate))
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)}_{n++}{Path.GetExtension(name)}";
        }

        names.Add(candidate);
    }

    private static string PickExtension(Random rng, IReadOnlyDictionary<string, int> weights, IReadOnlyList<string> keys)
    {
        var total = 0;
        foreach (var key in keys)
        {
            total += weights[key];
        }

        if (total <= 0)
        {
            return ".txt";
        }

        var pick = rng.Next(total);
        foreach (var key in keys)
        {
            pick -= weights[key];
            if (pick < 0)
            {
                return NormalizeExtension(key);
            }
        }

        return NormalizeExtension(keys[^1]);
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith('.') ? extension : "." + extension;
    }

    private static void WriteFile(string fullPath, DatasetProfile profile, int index, string fileName)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(ToExtendedPath(directory));
        }

        var payload = profile switch
        {
            DatasetProfile.Images when fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => TinyPng,
            DatasetProfile.Images => TinyJpeg,
            _ when index % 11 == 0 => [],
            _ => Encoding.UTF8.GetBytes($"FilesMate dataset entry {index}{Environment.NewLine}"),
        };

        File.WriteAllBytes(ToExtendedPath(fullPath), payload);
    }

    private static void WriteMarker(string markerPath, GenerationOptions options, int fileCount, int directoryCount)
    {
        var marker = new DatasetMarker
        {
            Seed = options.Seed,
            Profile = options.Profile.ToString(),
            FileCount = fileCount,
            DirectoryCount = directoryCount,
            Depth = options.Depth,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        var json = JsonSerializer.Serialize(marker, MarkerJsonContext.Default.DatasetMarker);
        File.WriteAllText(ToExtendedPath(markerPath), json, Encoding.UTF8);
    }

    private static void DeleteTree(string path)
    {
        var extended = ToExtendedPath(path);
        var info = new DirectoryInfo(extended);
        if (!info.Exists)
        {
            return;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        foreach (var file in info.EnumerateFiles("*", options))
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }

        foreach (var dir in info.EnumerateDirectories("*", options))
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                dir.Delete();
                continue;
            }

            DeleteTree(dir.FullName);
        }

        info.Attributes = FileAttributes.Directory;
        info.Delete();
    }

    private static void ThrowIfReparsePoint(string path)
    {
        var attributes = File.GetAttributes(ToExtendedPath(path));
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Refusing to use reparse point '{path}'.");
        }
    }

    internal static string ToExtendedPath(string fullPath)
    {
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return fullPath;
        }

        var full = Path.GetFullPath(fullPath);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + full[2..];
        }

        return @"\\?\" + full;
    }

    private static string CombineRelative(string parent, string name)
    {
        return string.IsNullOrEmpty(parent) ? name : parent + Path.DirectorySeparatorChar + name;
    }

    private static int GetDepth(string relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return 0;
        }

        var depth = 1;
        foreach (var c in relative)
        {
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
            {
                depth++;
            }
        }

        return depth;
    }
}
