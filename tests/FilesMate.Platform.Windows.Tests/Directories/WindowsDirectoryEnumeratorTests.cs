using FilesMate.Core.Directories;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.Platform.Windows.Tests.Directories;

public sealed class WindowsDirectoryEnumeratorTests
{
    [Fact]
    public async Task Stopping_after_first_batch_releases_a_producer_with_a_full_queue()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-enumerator-stop-").FullName;
        using var emergency = new CancellationTokenSource();
        try
        {
            for (var i = 0; i < 40; i++) File.WriteAllText(Path.Combine(root, "file-" + i), "fixture");
            var request = new DirectoryRequest(PaneId.New(), 1, root, new DirectoryReadOptions { BatchSize = 1 });
            var reader = new WindowsDirectoryEnumerator().EnumerateAsync(request, emergency.Token).GetAsyncEnumerator();
            Assert.True(await reader.MoveNextAsync());
            var dispose = reader.DisposeAsync().AsTask();
            try
            {
                await dispose.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(emergency.IsCancellationRequested);
            }
            finally { emergency.Cancel(); await dispose.WaitAsync(TimeSpan.FromSeconds(5)); }
            // The directory remains usable after the consumer stops early.
            Directory.Move(root, root + "-released");
            Directory.Move(root + "-released", root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Complete_enumeration_keeps_every_entry_and_final_batch()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-enumerator-complete-").FullName;
        try
        {
            var expected = Enumerable.Range(0, 40).Select(i => "file-" + i).ToHashSet();
            foreach (var name in expected) File.WriteAllText(Path.Combine(root, name), "fixture");
            var request = new DirectoryRequest(PaneId.New(), 1, root, new DirectoryReadOptions { BatchSize = 3 });
            var names = new HashSet<string>();
            var final = false;
            await foreach (var batch in new WindowsDirectoryEnumerator().EnumerateAsync(request, CancellationToken.None))
            {
                Assert.Null(batch.Error);
                foreach (var entry in batch.Entries) Assert.True(names.Add(entry.Name));
                final |= batch.IsFinal;
            }
            Assert.True(final);
            Assert.True(expected.SetEquals(names));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
