using FilesMate.App.Navigation;
using FilesMate.Core.Directories;
using FilesMate.Core.Entries;
using FilesMate.Core.Navigation;

namespace FilesMate.App.Tests.Navigation;

public sealed class TaggedDirectoryEnumeratorTests
{
    [Fact]
    public async Task Tag_location_lists_existing_paths_and_skips_missing_ones()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "tagged.txt");
        await File.WriteAllTextAsync(file, "ok");
        try
        {
            var enumerator = new TaggedDirectoryEnumerator(
                new ThrowingEnumerator(),
                (_, _) => Task.FromResult<IReadOnlyList<string>>([file, Path.Combine(directory, "missing.txt")]));

            var batch = Assert.Single(
                await enumerator.EnumerateAsync(
                    new DirectoryRequest(PaneId.New(), 1, TagLocation.Uri(4), DirectoryReadOptions.Default),
                    CancellationToken.None)
                    .ToListAsync());

            var entry = Assert.Single(batch.Entries);
            Assert.Equal(file, entry.Name);
            Assert.Equal(EntryKind.File, entry.Kind);
            Assert.True(batch.IsFinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Ordinary_folders_stay_on_the_inner_enumerator()
    {
        var inner = new RecordingEnumerator();
        var enumerator = new TaggedDirectoryEnumerator(
            inner,
            (_, _) => throw new InvalidOperationException("tags should not load"));

        _ = await enumerator.EnumerateAsync(
            new DirectoryRequest(PaneId.New(), 1, @"C:\Work", DirectoryReadOptions.Default),
            CancellationToken.None)
            .ToListAsync();

        Assert.Equal(@"C:\Work", inner.Path);
    }

    private sealed class ThrowingEnumerator : IDirectoryEnumerator
    {
        public IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
            DirectoryRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("inner enumerator should not run for tags");
    }

    private sealed class RecordingEnumerator : IDirectoryEnumerator
    {
        public string? Path { get; private set; }

        public async IAsyncEnumerable<DirectoryBatch> EnumerateAsync(
            DirectoryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Path = request.Path;
            await Task.CompletedTask;
            yield return DirectoryBatch.Create(
                request.PaneId,
                request.Generation,
                request.Path,
                [],
                isFinal: true,
                null);
        }
    }
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }
}
