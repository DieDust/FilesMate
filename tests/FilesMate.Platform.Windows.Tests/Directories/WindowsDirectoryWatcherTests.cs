using System.Diagnostics;

using FilesMate.Core.Directories;
using FilesMate.Core.Navigation;
using FilesMate.Platform.Windows.Directories;

namespace FilesMate.Platform.Windows.Tests.Directories;

public sealed class WindowsDirectoryWatcherTests
{
    [Fact]
    public async Task Paused_consumer_receives_overflow_after_a_file_event_burst()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-watch-burst-");
        try
        {
            await using var watcher = new WindowsDirectoryWatcher();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = new DirectoryRequest(PaneId.New(), 1, root.FullName, DirectoryReadOptions.Default);
            await using var stream = watcher.WatchAsync(request, deadline.Token).GetAsyncEnumerator();
            var first = stream.MoveNextAsync().AsTask();
            File.WriteAllText(Path.Combine(root.FullName, "ready.txt"), "ready");
            Assert.True(await first);
            // Deliberately stop consuming while the OS watcher continues producing.
            for (var i = 0; i < 2500; i++)
                File.WriteAllText(Path.Combine(root.FullName, $"item-{i}.txt"), "data");
            var overflow = false;
            for (var i = 0; i < 1026 && await stream.MoveNextAsync(); i++)
            {
                if (stream.Current.Kind != DirectoryWatchKind.Overflow) continue;
                overflow = true;
                break;
            }
            Assert.True(overflow);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task WatchAsync_reports_when_a_file_is_created()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-watch-");
        try
        {
            await using var watcher = new WindowsDirectoryWatcher();
            var request = new DirectoryRequest(PaneId.New(), 1, root.FullName, DirectoryReadOptions.Default);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            DirectoryWatchNotification? seen = null;
            var read = Task.Run(async () =>
            {
                await foreach (var notice in watcher.WatchAsync(request, cts.Token).ConfigureAwait(false))
                {
                    seen = notice;
                    break;
                }
            });

            var created = Path.Combine(root.FullName, "pkg.exe");
            var sw = Stopwatch.StartNew();
            while (seen is null && sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(40);
                if (!File.Exists(created))
                {
                    File.WriteAllText(created, "ok");
                }
            }

            await read.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.NotNull(seen);
            Assert.Equal(DirectoryWatchKind.Created, seen.Value.Kind);
            Assert.Equal("pkg.exe", seen.Value.Name, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task WatchAsync_reports_rename_old_and_new_name()
    {
        var root = Directory.CreateTempSubdirectory("filesmate-watch-ren-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "old.txt"), "ok");
            await using var watcher = new WindowsDirectoryWatcher();
            var request = new DirectoryRequest(PaneId.New(), 1, root.FullName, DirectoryReadOptions.Default);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            DirectoryWatchNotification? seen = null;
            var read = Task.Run(async () =>
            {
                await foreach (var notice in watcher.WatchAsync(request, cts.Token).ConfigureAwait(false))
                {
                    if (notice.Kind == DirectoryWatchKind.Renamed)
                    {
                        seen = notice;
                        break;
                    }
                }
            });

            var sw = Stopwatch.StartNew();
            var destination = Path.Combine(root.FullName, "new.txt");
            while (seen is null && sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(40);
                if (File.Exists(Path.Combine(root.FullName, "old.txt")) && !File.Exists(destination))
                {
                    File.Move(Path.Combine(root.FullName, "old.txt"), destination);
                }
            }

            await read.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.NotNull(seen);
            Assert.Equal("new.txt", seen.Value.Name, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("old.txt", seen.Value.OldName, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public async Task WatchAsync_disables_itself_when_the_folder_is_missing()
    {
        await using var watcher = new WindowsDirectoryWatcher();
        var request = new DirectoryRequest(
            PaneId.New(),
            1,
            Path.Combine(Path.GetTempPath(), "filesmate-missing-" + Guid.NewGuid().ToString("N")),
            DirectoryReadOptions.Default);
        await using var stream = watcher.WatchAsync(request, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(DirectoryWatchKind.WatcherDisabled, stream.Current.Kind);
        Assert.False(await stream.MoveNextAsync());
    }
}
