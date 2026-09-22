using FilesMate.App.Navigation;
using FilesMate.App.Commands;
using FilesMate.App.Services;
using FilesMate.Search;

namespace FilesMate.App.Tests.Navigation;

public sealed class SearchFileActionTests
{
    [Theory]
    [InlineData("CompressZip")]
    [InlineData("Compress7z")]
    [InlineData("CompressNew")]
    [InlineData("ExtractHere")]
    [InlineData("ExtractToFolder")]
    [InlineData("ExtractToOther")]
    [InlineData("SmartExtract")]
    [InlineData("OpenInCompactMate")]
    public void Search_archive_commands_reach_a_supported_main_window_archive_route(string command)
    {
        Assert.True(SearchFileAction.Supports(command));
        WithProfile(profile =>
        {
            var paths = new[] { @"C:\资料\one.zip", @"D:\another folder\two.zip" };
            var id = new SearchFileAction(command, paths).Write(profile);
            var launch = LaunchPath.Parse(["--search-action", id]);
            var request = SearchFileAction.Take(launch.SearchAction!, profile);
            Assert.Equal(command, request.Command);
            Assert.Equal(paths, request.Paths);
            Assert.True(Enum.TryParse<AppCommandId>(request.Command, out var action));
            Assert.NotNull(CompactMateSession.VerbFor(action));
            Assert.ThrowsAny<IOException>(() => SearchFileAction.Take(id, profile));
        });
    }

    [Fact]
    public void Action_launch_preserves_request_id_and_does_not_toggle_window()
    {
        var id = Guid.NewGuid().ToString("N");
        var launch = LaunchPath.Parse(["--search-action", id]);
        Assert.Equal(id, launch.SearchAction);
        Assert.True(launch.ActivateOnly);
        Assert.False(launch.SameDestination(launch with { SearchAction = Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public void Request_round_trips_multiple_folders_unicode_and_quotes_once()
    {
        WithProfile(profile =>
        {
            var request = new SearchFileAction("AddToShelf", [@"C:\资料\a b.txt", @"D:\另一个目录\c.txt"]);
            var id = request.Write(profile);
            var read = SearchFileAction.Take(id, profile);
            Assert.Equal(request.Command, read.Command);
            Assert.Equal(request.Paths, read.Paths);
            Assert.ThrowsAny<IOException>(() => SearchFileAction.Take(id, profile));
        });
    }

    [Theory]
    [InlineData("..\\elsewhere")]
    [InlineData("invalid")]
    public void Request_id_cannot_be_an_arbitrary_file_path(string id) =>
        Assert.Throws<ArgumentException>(() => SearchFileAction.Take(id));

    [Fact]
    public void Rejects_unsupported_commands_and_relative_paths()
    {
        Assert.Throws<ArgumentException>(() => new SearchFileAction("PermanentDelete", [@"C:\a.txt"]).Write());
        Assert.Throws<ArgumentException>(() => new SearchFileAction("Rename", ["a.txt"]).Write());
    }

    [Fact]
    public void Expired_requests_are_not_executed()
    {
        WithProfile(profile =>
        {
            var id = new SearchFileAction("Rename", [@"C:\a.txt"]).Write(profile);
            File.SetLastWriteTimeUtc(Path.Combine(profile, "search-actions", id + ".json"), DateTime.UtcNow.AddMinutes(-6));
            Assert.Throws<IOException>(() => SearchFileAction.Take(id, profile));
        });
    }

    private static void WithProfile(Action<string> test)
    {
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-action-test-" + Guid.NewGuid().ToString("N"));
        try { test(profile); }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }

    [Fact]
    public void Concurrent_receivers_cannot_execute_the_same_request_twice()
    {
        WithProfile(profile =>
        {
            var paths = Enumerable.Range(0, 512).Select(i => @"C:\资料\" + new string('a', 180) + i + ".txt").ToArray();
            var id = new SearchFileAction("AddToShelf", paths).Write(profile);
            var taken = 0;
            Parallel.For(0, 32, _ =>
            {
                try { SearchFileAction.Take(id, profile); Interlocked.Increment(ref taken); }
                // Windows can report access denied while the winning handle is delete-pending.
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            });
            Assert.Equal(1, taken);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(profile, "search-actions")));
        });
    }

    [Fact]
    public void Oversized_actions_are_rejected_before_creating_a_handoff_file()
    {
        WithProfile(profile =>
        {
            var paths = Enumerable.Repeat(@"C:\" + new string('日', 4096), 300).ToArray();
            Assert.Throws<ArgumentException>(() => new SearchFileAction("AddToShelf", paths).Write(profile));
            Assert.False(Directory.Exists(Path.Combine(profile, "search-actions")));
        });
    }
}
