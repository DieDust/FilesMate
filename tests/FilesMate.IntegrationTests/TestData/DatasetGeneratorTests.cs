using FilesMate.TestDataGenerator;

namespace FilesMate.IntegrationTests.TestData;

public sealed class DatasetGeneratorTests
{
    [Fact]
    public void Create_writes_exact_file_count_and_marker()
    {
        using var temp = new TempRoot();
        var generator = new DatasetGenerator();

        var result = generator.Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 37,
            DirectoryCount = 5,
            Depth = 2,
            Seed = 42,
            Profile = DatasetProfile.Mixed,
        });

        Assert.Equal(37, result.FileCount);
        Assert.Equal(37, CountFilesExcludingMarker(temp.Path));
        Assert.True(File.Exists(result.MarkerPath));
        Assert.Equal(DatasetGenerator.MarkerFileName, Path.GetFileName(result.MarkerPath));
        Assert.Contains("FilesMate.TestDataGenerator", File.ReadAllText(result.MarkerPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_is_deterministic_for_the_same_seed()
    {
        using var first = new TempRoot();
        using var second = new TempRoot();
        var generator = new DatasetGenerator();
        GenerationOptions OptionsFor(string root) => new()
        {
            Root = root,
            FileCount = 80,
            DirectoryCount = 8,
            Depth = 3,
            Seed = 20260829,
            Profile = DatasetProfile.Mixed,
        };

        var a = generator.Create(OptionsFor(first.Path));
        var b = generator.Create(OptionsFor(second.Path));

        Assert.Equal(a.RelativeFileNames, b.RelativeFileNames);
    }

    [Fact]
    public void Create_honors_mixed_extension_weights()
    {
        using var temp = new TempRoot();
        var generator = new DatasetGenerator();

        var result = generator.Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 50,
            DirectoryCount = 2,
            Depth = 1,
            Seed = 7,
            Profile = DatasetProfile.Mixed,
            ExtensionWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = 4,
                [".md"] = 1,
                [".json"] = 1,
            },
        });

        var extensions = result.RelativeFileNames
            .Select(static name => Path.GetExtension(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Subset(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md", ".json" }, extensions);
        Assert.Contains(result.RelativeFileNames, static name => name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.RelativeFileNames, static name => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.RelativeFileNames, static name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_unicode_profile_includes_special_name_categories()
    {
        using var temp = new TempRoot();
        var generator = new DatasetGenerator();

        var result = generator.Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 24,
            DirectoryCount = 3,
            Depth = 2,
            Seed = 11,
            Profile = DatasetProfile.Unicode,
        });

        var names = result.RelativeFileNames
            .Select(static relative => Path.GetFileName(relative) ?? string.Empty)
            .ToArray();
        Assert.Contains(names, static name => name.Contains("空文件", StringComparison.Ordinal));
        Assert.Contains(names, static name => name.Any(static c => c is >= '\u4E00' and <= '\u9FFF'));
        Assert.Contains(names, static name => name.Any(char.IsSurrogate));
        Assert.Contains(names, static name => name.Any(static c => c is >= '\u0590' and <= '\u05FF' or >= '\u0600' and <= '\u06FF'));
        Assert.Contains(names, static name => name.Contains('\u0301') || name.Normalize() != name);
        Assert.Contains(names, static name => name.Contains("CON", StringComparison.OrdinalIgnoreCase)
            || name.Contains("NUL", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AUX", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, static name => Path.GetFileNameWithoutExtension(name).Length >= 200);
    }

    [Fact]
    public void Create_does_not_escape_requested_root_or_depth()
    {
        using var temp = new TempRoot();
        var generator = new DatasetGenerator();

        var result = generator.Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 40,
            DirectoryCount = 12,
            Depth = 3,
            Seed = 3,
            Profile = DatasetProfile.Mixed,
        });

        var rootFull = Path.GetFullPath(temp.Path);
        foreach (var relative in result.RelativeFileNames)
        {
            Assert.False(Path.IsPathRooted(relative));
            var full = Path.GetFullPath(Path.Combine(rootFull, relative));
            Assert.StartsWith(rootFull, full, StringComparison.OrdinalIgnoreCase);
            var relativeFromRoot = Path.GetRelativePath(rootFull, full);
            var depth = relativeFromRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
            Assert.InRange(depth, 1, 4);
        }
    }

    [Fact]
    public void Create_images_profile_writes_valid_png_and_jpeg_files()
    {
        using var temp = new TempRoot();
        var generator = new DatasetGenerator();

        var result = generator.Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 16,
            DirectoryCount = 2,
            Depth = 1,
            Seed = 99,
            Profile = DatasetProfile.Images,
        });

        Assert.Equal(16, result.FileCount);
        Assert.Contains(result.RelativeFileNames, static name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.RelativeFileNames, static name => name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

        foreach (var relative in result.RelativeFileNames)
        {
            var bytes = File.ReadAllBytes(Path.Combine(temp.Path, relative));
            Assert.True(bytes.Length > 8);
            var isPng = bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';
            var isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8;
            Assert.True(isPng || isJpeg);
        }
    }

    [Fact]
    public void Clean_requires_marker_and_removes_generated_tree()
    {
        using var temp = new TempRoot();
        var generator = new DatasetGenerator();
        generator.Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 9,
            DirectoryCount = 2,
            Depth = 2,
            Seed = 1,
            Profile = DatasetProfile.Mixed,
        });

        generator.Clean(temp.Path, requireMarker: true);

        Assert.False(Directory.Exists(temp.Path));
    }

    [Fact]
    public void Clean_without_marker_is_rejected()
    {
        using var temp = new TempRoot();
        File.WriteAllText(Path.Combine(temp.Path, "unrelated.txt"), "no");
        var generator = new DatasetGenerator();

        var ex = Assert.Throws<InvalidOperationException>(() => generator.Clean(temp.Path, requireMarker: true));
        Assert.Contains("marker", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "unrelated.txt")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative\\folder")]
    [InlineData("C:\\")]
    public void Create_rejects_empty_relative_and_drive_root_paths(string root)
    {
        var generator = new DatasetGenerator();
        Assert.ThrowsAny<ArgumentException>(() => generator.Create(new GenerationOptions
        {
            Root = root,
            FileCount = 1,
            Seed = 1,
            Profile = DatasetProfile.Empty,
        }));
    }

    [Fact]
    public void Create_rejects_user_profile_and_workspace_root()
    {
        var generator = new DatasetGenerator();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.ThrowsAny<ArgumentException>(() => generator.Create(new GenerationOptions
        {
            Root = profile,
            FileCount = 1,
            Seed = 1,
            Profile = DatasetProfile.Empty,
        }));

        var workspace = FindWorkspaceRoot();
        Assert.ThrowsAny<ArgumentException>(() => generator.Create(new GenerationOptions
        {
            Root = workspace,
            FileCount = 1,
            Seed = 1,
            Profile = DatasetProfile.Empty,
        }));
    }

    [Fact]
    public void Create_empty_profile_has_marker_and_no_payload_files()
    {
        using var temp = new TempRoot();
        var result = new DatasetGenerator().Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 0,
            DirectoryCount = 0,
            Depth = 1,
            Seed = 5,
            Profile = DatasetProfile.Empty,
        });

        Assert.Equal(0, result.FileCount);
        Assert.Equal(0, CountFilesExcludingMarker(temp.Path));
        Assert.True(File.Exists(result.MarkerPath));
    }

    private static int CountFilesExcludingMarker(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Count(static path => !string.Equals(Path.GetFileName(path), DatasetGenerator.MarkerFileName, StringComparison.Ordinal));
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FilesMate.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("FilesMate.slnx was not found.");
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FilesMateTests",
            Guid.NewGuid().ToString("N"));

        public TempRoot()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    var generator = new DatasetGenerator();
                    var marker = System.IO.Path.Combine(Path, DatasetGenerator.MarkerFileName);
                    if (File.Exists(marker))
                    {
                        generator.Clean(Path, requireMarker: true);
                    }
                    else
                    {
                        Directory.Delete(Path, recursive: true);
                    }
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
