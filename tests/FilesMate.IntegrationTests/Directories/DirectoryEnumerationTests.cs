using System.IO;

using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;
using FilesMate.TestDataGenerator;

namespace FilesMate.IntegrationTests.Directories;

public sealed class DirectoryEnumerationTests
{
    [Fact]
    public async Task Empty_directory_emits_one_final_batch()
    {
        using var temp = new TempDir();
        var batches = await EnumerateAsync(temp.Path, DirectoryReadOptions.Default);

        var batch = Assert.Single(batches);
        Assert.True(batch.IsFinal);
        Assert.Empty(batch.Entries);
        Assert.Null(batch.Error);
    }

    [Fact]
    public async Task One_thousand_entries_arrive_in_multiple_batches()
    {
        using var temp = new TempDir();
        new DatasetGenerator().Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 1000,
            DirectoryCount = 0,
            Depth = 1,
            Seed = 9,
            Profile = DatasetProfile.Mixed,
        });

        var options = DirectoryReadOptions.Default with { BatchSize = 256, BatchDeadline = TimeSpan.FromMilliseconds(8) };
        var batches = await EnumerateAsync(temp.Path, options);

        Assert.True(batches.Count >= 4);
        Assert.True(batches[^1].IsFinal);
        Assert.All(batches.Take(batches.Count - 1), static batch => Assert.False(batch.IsFinal));
        var files = batches.SelectMany(static batch => batch.Entries)
            .Where(static e => e.Kind == EntryKind.File && e.Name != DatasetGenerator.MarkerFileName)
            .ToArray();
        Assert.Equal(1000, files.Length);
        Assert.DoesNotContain(batches.SelectMany(static b => b.Entries), static e => e.Name is "." or "..");
    }

    [Fact]
    public async Task Hidden_system_and_readonly_attributes_are_preserved()
    {
        using var temp = new TempDir();
        var hidden = Path.Combine(temp.Path, "hidden.txt");
        var system = Path.Combine(temp.Path, "system.txt");
        var readOnly = Path.Combine(temp.Path, "readonly.txt");
        File.WriteAllText(hidden, "h");
        File.WriteAllText(system, "s");
        File.WriteAllText(readOnly, "r");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        var entries = (await EnumerateAsync(temp.Path, DirectoryReadOptions.Default))
            .SelectMany(static b => b.Entries)
            .ToDictionary(static e => e.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(entries["hidden.txt"].Attributes.HasFlag(FileAttributes.Hidden));
        Assert.True(entries["system.txt"].Attributes.HasFlag(FileAttributes.System));
        Assert.True(entries["readonly.txt"].Attributes.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task Directories_do_not_include_child_counts()
    {
        using var temp = new TempDir();
        var child = Directory.CreateDirectory(Path.Combine(temp.Path, "folder"));
        File.WriteAllText(Path.Combine(child.FullName, "a.txt"), "a");
        File.WriteAllText(Path.Combine(child.FullName, "b.txt"), "b");

        var folder = (await EnumerateAsync(temp.Path, DirectoryReadOptions.Default))
            .SelectMany(static b => b.Entries)
            .Single(static e => e.Kind == EntryKind.Directory);

        Assert.Equal("folder", folder.Name);
        Assert.Equal(0UL, folder.Size);
    }

    [Fact]
    public async Task Cancellation_stops_further_batches()
    {
        using var temp = new TempDir();
        new DatasetGenerator().Create(new GenerationOptions
        {
            Root = temp.Path,
            FileCount = 800,
            DirectoryCount = 0,
            Depth = 1,
            Seed = 4,
            Profile = DatasetProfile.Mixed,
        });

        var options = DirectoryReadOptions.Default with { BatchSize = 32 };
        using var cts = new CancellationTokenSource();
        var enumerator = new WindowsDirectoryEnumerator();
        var request = new DirectoryRequest(PaneId.New(), 1, temp.Path, options);
        var seen = 0;
        try
        {
            await foreach (var batch in enumerator.EnumerateAsync(request, cts.Token))
            {
                seen++;
                if (seen == 1)
                {
                    await cts.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(seen >= 1);
        Assert.True(seen < 25);
    }

    [Fact]
    public async Task Access_denied_maps_to_typed_error()
    {
        const string deniedPath = @"C:\System Volume Information";
        if (!Directory.Exists(deniedPath))
        {
            return;
        }

        var batches = await EnumerateAsync(deniedPath, DirectoryReadOptions.Default);
        var final = Assert.Single(batches);
        Assert.True(final.IsFinal);
        Assert.NotNull(final.Error);
        Assert.Equal(DirectoryReadErrorKind.AccessDenied, final.Error.Kind);
        Assert.True(final.Error.IsTerminal);
    }

    [Fact]
    public async Task Deleting_files_during_enumeration_still_completes()
    {
        using var temp = new TempDir();
        var files = Enumerable.Range(0, 200)
            .Select(i => Path.Combine(temp.Path, $"n{i:D3}.txt"))
            .ToArray();
        foreach (var file in files)
        {
            File.WriteAllText(file, "x");
        }

        var options = DirectoryReadOptions.Default with { BatchSize = 20 };
        var enumerator = new WindowsDirectoryEnumerator();
        var request = new DirectoryRequest(PaneId.New(), 1, temp.Path, options);
        var batches = new List<DirectoryBatch>();
        var index = 0;
        await foreach (var batch in enumerator.EnumerateAsync(request, CancellationToken.None))
        {
            batches.Add(batch);
            if (index++ == 0)
            {
                foreach (var file in files.Take(50))
                {
                    File.Delete(file);
                }
            }
        }

        Assert.True(batches[^1].IsFinal);
        Assert.True(batches.SelectMany(static b => b.Entries).Any());
    }

    private static async Task<List<DirectoryBatch>> EnumerateAsync(string path, DirectoryReadOptions options)
    {
        var enumerator = new WindowsDirectoryEnumerator();
        var request = new DirectoryRequest(PaneId.New(), 1, path, options);
        var batches = new List<DirectoryBatch>();
        await foreach (var batch in enumerator.EnumerateAsync(request, CancellationToken.None))
        {
            batches.Add(batch);
        }

        return batches;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FilesMateEnum",
            Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
