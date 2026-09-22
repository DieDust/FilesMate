using System.Diagnostics;
using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Search;

public sealed class SearchIndexLifecycleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("FilesMate-index-lifecycle-").FullName;
    private string Database => Path.Combine(_root, "database", SearchIndexSettings.DatabaseFileName);
    private string Content => Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
    private SearchIndexSettings Settings => SearchIndexSettings.Sanitize([Content], [], false, maxDepth: 0);

    [Fact]
    public async Task Independent_search_finishes_on_old_snapshot_then_rebuild_publishes_new_snapshot()
    {
        await using var index = await CreateIndexAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var search = HoldIndependentReader(entered, release);
        Task? rebuild = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            File.WriteAllText(Path.Combine(Content, "new-report.txt"), "new");
            rebuild = index.RebuildAsync(Settings);
            await WaitForCompletedStagingAsync(index, rebuild);
            await Task.Delay(100);
            Assert.False(rebuild.IsCompleted);
            release.Set();
            Assert.Single(await search);
            await rebuild;
            Assert.Single(NameIndexReader.Search(Database, "new-report", null, 80));
            Assert.Null(index.StorageError);
            Assert.False(index.IsRunning);
            AssertNoStaging();
        }
        finally { release.Set(); await search; if (rebuild is not null) await Record.ExceptionAsync(() => rebuild); }
    }

    [Fact]
    public async Task Cancel_while_waiting_for_independent_reader_keeps_old_index_and_cleans_staging()
    {
        await using var index = await CreateIndexAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var search = HoldIndependentReader(entered, release);
        Task? rebuild = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            File.WriteAllText(Path.Combine(Content, "new-report.txt"), "new");
            rebuild = index.RebuildAsync(Settings, cancellation.Token);
            await WaitForCompletedStagingAsync(index, rebuild);
            var watch = Stopwatch.StartNew();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rebuild);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
            Assert.Single(NameIndexReader.Search(Database, "old-report", null, 80));
            Assert.Empty(NameIndexReader.Search(Database, "new-report", null, 80));
            Assert.False(index.IsRunning);
            AssertNoStaging();
        }
        finally { release.Set(); await search; if (rebuild is not null) await Record.ExceptionAsync(() => rebuild); }
    }

    [Fact]
    public async Task Database_change_while_waiting_aborts_publication_instead_of_overwriting_it()
    {
        await using var index = await CreateIndexAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var search = HoldIndependentReader(entered, release);
        Task? rebuild = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            File.WriteAllText(Path.Combine(Content, "new-report.txt"), "new");
            rebuild = index.RebuildAsync(Settings);
            await WaitForCompletedStagingAsync(index, rebuild);
            File.SetLastWriteTimeUtc(Database, DateTime.UtcNow.AddMinutes(-1));
            var error = await Assert.ThrowsAsync<IOException>(() => rebuild);
            Assert.Contains("changed", error.Message, StringComparison.Ordinal);
            Assert.Empty(NameIndexReader.Search(Database, "new-report", null, 80));
            Assert.Single(NameIndexReader.Search(Database, "old-report", null, 80));
            AssertNoStaging();
        }
        finally { release.Set(); await search; if (rebuild is not null) await Record.ExceptionAsync(() => rebuild); }
    }

    [Fact]
    public async Task Reader_lock_timeout_keeps_old_database_and_removes_only_completed_staging()
    {
        await using var index = await CreateIndexAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var search = HoldIndependentReader(entered, release);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            File.WriteAllText(Path.Combine(Content, "new-report.txt"), "new");
            var watch = Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<IOException>(() => index.RebuildAsync(Settings));
            Assert.True((error.HResult & 0xFFFF) is 32 or 33);
            Assert.InRange(watch.Elapsed.TotalSeconds, 4.9, 15);
            Assert.Single(NameIndexReader.Search(Database, "old-report", null, 80));
            Assert.Empty(NameIndexReader.Search(Database, "new-report", null, 80));
            AssertNoStaging();
        }
        finally { release.Set(); await search; }
    }

    [Fact]
    public async Task Same_search_provider_observes_migration_and_preserves_rank_update_from_another_component()
    {
        var index = await CreateIndexAsync();
        try
        {
            var configuration = new SearchIndexSettingsService(Path.Combine(_root, "search-index.json"));
            await configuration.SaveAsync(Settings with { DatabaseDirectory = Path.GetDirectoryName(Database)! }, updateDatabaseDirectory: true);
            var staleSettings = configuration.Load();
            var provider = new IndexedFilesSearchProvider(_root);
            Assert.Single((await provider.SearchAsync("old-report", default)).Hits);
            await index.DisposeAsync();
            var destination = Path.Combine(_root, "migrated", SearchIndexSettings.DatabaseFileName);
            await SearchIndexStorage.MigrateAsync(Database, destination,
                (path, token) => configuration.UpdateDatabaseDirectoryAsync(Path.GetDirectoryName(path)!, token));
            SearchRankingConfiguration.Save([SearchHitKind.Folder, SearchHitKind.Document], _root);
            await configuration.SaveAsync(staleSettings with { AutoRefresh = false });
            Assert.Equal(destination, GlobalSearchConfiguration.ResolveDatabase(_root));
            Assert.Equal(SearchHitKind.Folder, configuration.Load().RankOrder[0]);
            Assert.Single((await provider.SearchAsync("old-report", default)).Hits);
            await using var reloaded = new FileNameIndexService(destination);
            File.WriteAllText(Path.Combine(Content, "new-report.txt"), "new");
            await reloaded.RebuildAsync(configuration.Load());
            Assert.Single((await provider.SearchAsync("new-report", default)).Hits);
        }
        finally { await index.DisposeAsync(); }
    }

    [Fact]
    public async Task Rebuilt_compact_index_preserves_ordinal_unicode_search_and_paging()
    {
        string[] names = ["报告🎉甲.txt", "报告🎉乙.txt", "資料🇯🇵.txt", "Budget-Q3.txt", "𝒜pp𝒷.txt", "𐐀𐐨-report.txt", "space and-é.txt"];
        foreach (var name in names) File.WriteAllText(Path.Combine(Content, name), "");
        await using var index = new FileNameIndexService(Database);
        await index.RebuildAsync(Settings);
        foreach (var query in new[] { "报", "报告", "🎉", "报告🎉", "資料", "🇯🇵", "budget", "Q3", "𝒜pp", "𐐨", "é", "REPORT", "\ud83c", "\udf89" })
        {
            var expected = names.Where(name => NameIndexReader.Matches(name, NameIndexReader.Terms(query))).Order(StringComparer.Ordinal).ToArray();
            var actual = NameIndexReader.Search(Database, query, null, 80).Select(hit => hit.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
        }
        var first = NameIndexReader.Search(Database, "报告", null, 1);
        var second = NameIndexReader.Search(Database, "报告", null, 1, offset: 1);
        Assert.Single(first);
        Assert.Single(second);
        Assert.NotEqual(first[0].Path, second[0].Path);
        using var connection = Open(Database);
        using var vocabulary = connection.CreateCommand();
        vocabulary.CommandText = "CREATE VIRTUAL TABLE temp.original_terms USING fts5vocab(main,file_name,'row'); SELECT count(*) FROM temp.original_terms;";
        Assert.Equal(0L, vocabulary.ExecuteScalar());
        SearchIndexStorage.ValidateDatabase(Database, checkIntegrity: true);
    }

    private async Task<FileNameIndexService> CreateIndexAsync()
    {
        File.WriteAllText(Path.Combine(Content, "old-report.txt"), "old");
        var index = new FileNameIndexService(Database);
        await index.RebuildAsync(Settings);
        return index;
    }

    private Task<IReadOnlyList<NameHit>> HoldIndependentReader(ManualResetEventSlim entered, ManualResetEventSlim release) =>
        Task.Run(() => NameIndexReader.Search(Database, "old-report", null, 80, rank: (_, _) =>
        { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); return 0; }));

    private static async Task WaitForCompletedStagingAsync(FileNameIndexService index, Task rebuild)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (rebuild.IsCompleted) { await rebuild; throw new IOException("Rebuild ended before its reader lock was released."); }
            if (index.BuildingPath is { } staging && File.Exists(staging))
            {
                try
                {
                    using var connection = Open(staging);
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT value FROM index_meta WHERE key='completed';";
                    if (command.ExecuteScalar() is string) return;
                }
                catch (SqliteException) { }
            }
            await Task.Delay(10);
        }
        throw new TimeoutException("Rebuild did not finish preparing its snapshot.");
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open();
        return connection;
    }

    private void AssertNoStaging() => Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(Database)!, "*.rebuild-*"));
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
