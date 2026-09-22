using FilesMate.App.Services;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Services;

public sealed class SearchIndexStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FilesMate-index-storage-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_directory, "source.db");
    private string Destination => Path.Combine(_directory, "destination", "search-index.db");

    public SearchIndexStorageTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Usage_includes_database_journals_and_build_output_without_double_counting()
    {
        var build = Source + ".building";
        File.WriteAllBytes(Source, new byte[11]);
        File.WriteAllBytes(Source + "-wal", new byte[17]);
        File.WriteAllBytes(Source + "-shm", new byte[19]);
        File.WriteAllBytes(Source + "-journal", new byte[23]);
        File.WriteAllBytes(build, new byte[29]);
        Assert.Equal(new SearchIndexUsage(99, true), SearchIndexStorage.ReadUsage(Source, build));
        Assert.Equal(new SearchIndexUsage(70, true), SearchIndexStorage.ReadUsage(Source, Source));
        Assert.Equal(new SearchIndexUsage(0, true), SearchIndexStorage.ReadUsage(Path.Combine(_directory, "missing.db")));
    }

    [Fact]
    public void An_inaccessible_component_marks_usage_unavailable_instead_of_displaying_zero()
    {
        File.WriteAllBytes(Source, new byte[11]);
        Directory.CreateDirectory(Source + "-wal");
        Assert.Equal(new SearchIndexUsage(11, false), SearchIndexStorage.ReadUsage(Source));
    }

    [Fact]
    public void Schema_validation_never_initializes_an_unrelated_database()
    {
        using (var database = Open(Source)) Execute(database, "CREATE TABLE important_data(value TEXT); INSERT INTO important_data VALUES('keep');");
        var before = File.ReadAllBytes(Source);
        Assert.Throws<InvalidDataException>(() => SearchIndexStorage.ValidateDatabase(Source));
        Assert.Equal(before, File.ReadAllBytes(Source));
        Assert.ThrowsAny<IOException>(() => SearchIndexStorage.ValidateDatabase(Destination));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Migration_takes_a_consistent_backup_of_committed_wal_rows()
    {
        using var writer = CreateIndex(Source);
        Execute(writer, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO file_name VALUES('wal-only.txt','C:/wal-only.txt','0');");
        Assert.True(new FileInfo(Source + "-wal").Length > 0);
        var callbackCount = 0;
        var result = await SearchIndexStorage.MigrateAsync(Source, Destination, (path, _) =>
        {
            callbackCount++;
            using var target = Open(path, SqliteOpenMode.ReadOnly);
            Assert.Equal("wal-only.txt", Scalar(target, "SELECT name FROM file_name;"));
            return Task.CompletedTask;
        });
        Assert.Equal(1, callbackCount);
        Assert.True(result.OldFilesRetained);
        Assert.Equal("SourceRetainedForSafety", result.RetentionReason);
        Assert.True(File.Exists(Source));
        Assert.True(File.Exists(Source + "-wal"));
        SearchIndexStorage.ValidateDatabase(Destination, checkIntegrity: true);
    }

    [Fact]
    public async Task Unshared_database_moves_after_settings_are_committed()
    {
        using (CreateIndex(Source)) { }
        var settingsPublished = false;
        var result = await SearchIndexStorage.MigrateAsync(Source, Destination, (path, _) =>
        {
            Assert.True(File.Exists(Source));
            Assert.Equal(Destination, path);
            settingsPublished = true;
            return Task.CompletedTask;
        });
        Assert.True(settingsPublished);
        Assert.True(File.Exists(Destination));
        if (OperatingSystem.IsWindows())
        {
            Assert.False(result.OldFilesRetained);
            Assert.False(File.Exists(Source));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData("-journal")]
    [InlineData("directory")]
    public async Task Migration_refuses_existing_destination_files_or_directories(string suffix)
    {
        using (CreateIndex(Source)) { }
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        if (suffix == "directory") Directory.CreateDirectory(Destination);
        else File.WriteAllText(Destination + suffix, "unrelated data");
        var called = false;
        await Assert.ThrowsAsync<IOException>(() => SearchIndexStorage.MigrateAsync(Source, Destination, (_, _) => { called = true; return Task.CompletedTask; }));
        Assert.False(called);
        Assert.True(File.Exists(Source));
        if (suffix != "directory") Assert.Equal("unrelated data", File.ReadAllText(Destination + suffix));
    }

    [Fact]
    public async Task Failed_settings_publication_rolls_back_only_the_new_copy()
    {
        using (CreateIndex(Source)) { }
        var before = File.ReadAllBytes(Source);
        await Assert.ThrowsAsync<IOException>(() => SearchIndexStorage.MigrateAsync(Source, Destination,
            (_, _) => Task.FromException(new IOException("settings publication failed"))));
        Assert.Equal(before, File.ReadAllBytes(Source));
        if (OperatingSystem.IsWindows()) Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Destination)!));
    }

    [Fact]
    public async Task An_external_file_cannot_replace_the_destination_during_publication()
    {
        using (CreateIndex(Source)) { }
        var replacement = Path.Combine(_directory, "external.txt");
        File.WriteAllText(replacement, "external file");
        var error = await Record.ExceptionAsync(() => SearchIndexStorage.MigrateAsync(Source, Destination, (path, _) =>
        {
            File.Move(replacement, path, overwrite: true);
            throw new IOException("settings failed");
        }));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("external file", File.ReadAllText(replacement));
        if (OperatingSystem.IsWindows()) Assert.False(File.Exists(Destination));
        Assert.True(File.Exists(Source));
    }

    [Fact]
    public async Task Cancellation_before_publication_keeps_the_current_index()
    {
        using (CreateIndex(Source)) { }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchIndexStorage.MigrateAsync(Source, Destination,
            (_, _) => throw new Exception("must not publish"), cancellation.Token));
        Assert.True(File.Exists(Source));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Cancelled_settings_publication_removes_the_copy_and_retains_source()
    {
        using (CreateIndex(Source)) { }
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchIndexStorage.MigrateAsync(Source, Destination, (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, cancellation.Token));
        Assert.True(File.Exists(Source));
        if (OperatingSystem.IsWindows()) Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task A_first_index_location_can_be_published_without_an_existing_database()
    {
        var result = await SearchIndexStorage.MigrateAsync(Source, Destination, (_, _) => Task.CompletedTask);
        Assert.False(result.OldFilesRetained);
        Assert.Null(result.RetentionReason);
        Assert.False(File.Exists(Source));
        SearchIndexStorage.ValidateDatabase(Destination, checkIntegrity: true);
    }

    [Fact]
    public async Task Cancellation_after_successful_settings_commit_does_not_remove_the_active_database()
    {
        using (CreateIndex(Source)) { }
        using var cancellation = new CancellationTokenSource();
        var result = await SearchIndexStorage.MigrateAsync(Source, Destination, (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);
        Assert.Equal(Destination, result.DatabasePath);
        SearchIndexStorage.ValidateDatabase(Destination, checkIntegrity: true);
    }

    [Theory]
    [InlineData("2", "CREATE VIRTUAL TABLE file_name_substring USING fts5(name,tokenize='trigram');")]
    [InlineData("3", "CREATE VIRTUAL TABLE file_name_gram USING fts5(grams,content='',tokenize='ascii',detail='none',columnsize=0);")]
    public void Schema_validation_accepts_supported_secondary_indexes(string version, string createSearchTable)
    {
        using (var connection = CreateIndex(Source))
        {
            Execute(connection, createSearchTable);
            using var metadata = connection.CreateCommand();
            metadata.CommandText = "INSERT INTO index_meta VALUES('schema',$version);";
            metadata.Parameters.AddWithValue("$version", version);
            metadata.ExecuteNonQuery();
        }
        SearchIndexStorage.ValidateDatabase(Source, checkIntegrity: true);
    }

    [Fact]
    public void Schema_validation_rejects_a_future_version_without_rewriting_it()
    {
        using (var connection = CreateIndex(Source)) Execute(connection, "INSERT INTO index_meta VALUES('schema','999');");
        var before = File.ReadAllBytes(Source);
        Assert.Throws<InvalidDataException>(() => SearchIndexStorage.ValidateDatabase(Source));
        Assert.Equal(before, File.ReadAllBytes(Source));
    }

    [Fact]
    public async Task An_external_reader_of_the_old_database_prevents_cleanup()
    {
        using (CreateIndex(Source)) { }
        FileStream? reader = null;
        var result = await SearchIndexStorage.MigrateAsync(Source, Destination, (_, _) =>
        {
            reader = new FileStream(Source, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.CompletedTask;
        });
        try { Assert.True(result.OldFilesRetained); Assert.True(File.Exists(Source)); }
        finally { reader?.Dispose(); }
    }

    private static SqliteConnection CreateIndex(string path)
    {
        var connection = Open(path);
        Execute(connection, "CREATE TABLE index_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE VIRTUAL TABLE file_name USING fts5(name,path,is_dir UNINDEXED);");
        return connection;
    }
    private static SqliteConnection Open(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static void Execute(SqliteConnection database, string sql)
    { using var command = database.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection database, string sql)
    { using var command = database.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    public void Dispose() { Directory.Delete(_directory, recursive: true); }
}
