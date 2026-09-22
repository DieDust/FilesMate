using FilesMate.App.Models;
using FilesMate.App.Services;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Search;

public sealed class IndexStorageSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-index-safety-" + Guid.NewGuid().ToString("N"));
    public IndexStorageSafetyTests() => Directory.CreateDirectory(_root);
    private string Database => Path.Combine(_root, "search-index.db");
    private SearchIndexSettings Settings => SearchIndexSettings.Sanitize([_root], [], false, 1, _root);

    [Fact]
    public async Task Current_folder_search_works_before_the_first_index_build()
    {
        var target = Path.Combine(_root, "fresh-start-needle.txt");
        File.WriteAllText(target, "test");
        await using var index = new FileNameIndexService(Database);
        var hits = await index.SearchAsync("fresh-start-needle", _root);
        Assert.Contains(hits, hit => hit.Path == target);
        Assert.False(File.Exists(Database));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Loading_and_rebuilding_preserve_foreign_files(bool sqlite)
    {
        if (sqlite)
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE personal_data(value TEXT); INSERT INTO personal_data VALUES('keep me');";
            command.ExecuteNonQuery();
        }
        else File.WriteAllText(Database, "Unrelated user data");
        var before = File.ReadAllBytes(Database);
        await using var index = new FileNameIndexService(Database);
        Assert.NotNull(index.StorageError);
        Assert.Equal(before, File.ReadAllBytes(Database));
        var events = new List<SearchIndexProgress>();
        index.ProgressChanged += (_, progress) => events.Add(progress);
        await Assert.ThrowsAnyAsync<Exception>(() => index.RebuildAsync(Settings));
        Assert.Equal(before, File.ReadAllBytes(Database));
        Assert.False(index.IsRunning);
        Assert.NotNull(events.Last().Error);
        Assert.False(events.Last().Running);
    }

    [Fact]
    public async Task Rebuild_does_not_replace_file_created_while_scanning()
    {
        File.WriteAllText(Path.Combine(_root, "fixture.txt"), "test");
        await using var index = new FileNameIndexService(Database);
        index.ProgressChanged += (_, progress) =>
        {
            if (progress.Running && progress.CurrentPath is not null && !File.Exists(Database))
                File.WriteAllText(Database, "created by another program");
        };
        await Assert.ThrowsAsync<IOException>(() => index.RebuildAsync(Settings));
        Assert.Equal("created by another program", File.ReadAllText(Database));
        Assert.Null(index.BuildingPath);
        Assert.Empty(Directory.GetFiles(_root, "*.rebuild-*"));
    }

    [Fact]
    public async Task Successful_rebuild_reports_completion_but_cancel_does_not()
    {
        File.WriteAllText(Path.Combine(_root, "fixture.txt"), "test");
        await using var index = new FileNameIndexService(Database);
        SearchIndexProgress latest = default;
        index.ProgressChanged += (_, progress) => latest = progress;
        await index.RebuildAsync(Settings);
        Assert.NotNull(latest.CompletedUtc);
        Assert.False(latest.Cancelled);
        Assert.Null(latest.Error);
        Assert.Equal(index.Stats.CompletedUtc, latest.CompletedUtc);
        var before = File.ReadAllBytes(Database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => index.RebuildAsync(Settings, cancellation.Token));
        Assert.True(latest.Cancelled);
        Assert.False(latest.Running);
        Assert.Equal(before, File.ReadAllBytes(Database));
    }

    public void Dispose()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "FilesMate-index-safety-");
        if (Path.GetFullPath(_root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
