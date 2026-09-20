using System.Diagnostics;
using System.Globalization;

using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.Search;

using Microsoft.Data.Sqlite;

namespace FilesMate.App.Services;

public sealed record FileNameSearchResponse(IReadOnlyList<HomeSearchHit> Hits, bool Partial);

public sealed class FileNameIndexService : IFileNameSearchIndex
{
    public static string DefaultFilePath => Path.Combine(
        SearchIndexSettings.DefaultDatabaseDirectory,
        SearchIndexSettings.DatabaseFileName);

    internal const int ResultLimit = 80;

    internal const int FetchLimit = 240;

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private readonly object _lifetimeGate = new();
    private CancellationTokenSource? _runCts;
    private bool _disposed;

    public FileNameIndexService(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Index database path is required.", nameof(filePath));
        }

        FilePath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();

        Stats = LoadStats();
    }

    public string FilePath { get; }

    public bool IsRunning { get; private set; }

    public bool NeedsUpgrade { get; private set; }

    public SearchIndexStats Stats { get; private set; }

    public event EventHandler<SearchIndexProgress>? ProgressChanged;

    public async Task<IReadOnlyList<HomeSearchHit>> SearchAsync(
        string query, string? directory = null, IReadOnlyList<SearchHitKind>? rankOrder = null,
        CancellationToken cancellationToken = default) =>
        (await SearchWithStatusAsync(query, directory, rankOrder, cancellationToken).ConfigureAwait(false)).Hits;

    public async Task<FileNameSearchResponse> SearchWithStatusAsync(
        string query, string? directory = null, IReadOnlyList<SearchHitKind>? rankOrder = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(query) || !TryGetPathPrefix(directory, out var prefix)) return new([], false);
        IReadOnlyList<HomeSearchHit> indexed;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            indexed = await Task.Run(() => NameIndexReader.Search(FilePath, query, prefix, FetchLimit, cancellationToken, SearchHitRanking.CreateRank(rankOrder, query), rankByPath: true)
                .Select(hit => new HomeSearchHit(hit.Name, hit.Path, hit.IsDirectory)).ToArray(), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }

        // Overlay a bounded, cancelable live walk for current-folder searches, so
        // newly created files and folders outside the configured index are usable.
        var live = prefix is not null || Stats.Files + Stats.Folders == 0
            ? await Task.Run(() => SearchLiveDetailed(query,
                prefix is null ? HomePlaces.UserFolders().Select(item => item.Path) : [prefix],
                FetchLimit, cancellationToken), cancellationToken).ConfigureAwait(false)
            : new FileNameSearchResponse([], false);
        var combined = live.Hits.Concat(indexed).DistinctBy(hit => hit.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(SearchHitRanking.Sort(combined, rankOrder, ResultLimit, query),
            live.Partial || combined.Length >= ResultLimit);
    }

    public async Task RebuildAsync(SearchIndexSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CancellationTokenSource run;
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _runCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runCts = run;
        }

        IsRunning = true;
        Raise(new SearchIndexProgress(null, 0, 0, 0, true));
        var enteredGate = false;
        try
        {
            await _rebuildGate.WaitAsync(run.Token).ConfigureAwait(false);
            enteredGate = true;
            Stats = await Task.Run(() => RebuildCore(settings, run.Token), run.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentRun(run))
            {
                Raise(new SearchIndexProgress(null, Stats.Files, Stats.Folders, Stats.Errors, false));
            }

            throw;
        }
        finally
        {
            if (enteredGate)
            {
                _rebuildGate.Release();
            }

            lock (_lifetimeGate)
            {
                if (ReferenceEquals(_runCts, run))
                {
                    _runCts = null;
                    IsRunning = false;
                }
            }

            run.Dispose();
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? run;
        lock (_lifetimeGate)
        {
            run = _runCts;
        }

        try
        {
            run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? run;
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            run = _runCts;
        }

        try
        {
            run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        await _rebuildGate.WaitAsync().ConfigureAwait(false);
        _rebuildGate.Release();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private bool IsCurrentRun(CancellationTokenSource run)
    {
        lock (_lifetimeGate)
        {
            return ReferenceEquals(_runCts, run);
        }
    }

    public static IReadOnlyList<HomeSearchHit> SearchLive(string query, IEnumerable<string> roots, int limit,
        CancellationToken cancellationToken) => SearchLiveDetailed(query, roots, limit, cancellationToken).Hits;

    private static FileNameSearchResponse SearchLiveDetailed(string query, IEnumerable<string> roots, int limit,
        CancellationToken token)
    {
        var hits = new List<HomeSearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(roots);
        var watch = Stopwatch.StartNew();
        var terms = NameIndexReader.Terms(query);
        var partial = false;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        while (queue.TryDequeue(out var folder))
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(folder)) continue;
            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (hits.Count >= limit || watch.ElapsedMilliseconds > 1500) return new(hits, true);
                    var directory = (entry.Attributes & FileAttributes.Directory) != 0;
                    if (NameIndexReader.Matches(entry.Name, terms)) hits.Add(new(entry.Name, entry.FullName, directory));
                    if (directory) queue.Enqueue(entry.FullName);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { partial = true; }
        }
        return new(hits, partial);
    }

    internal static bool TryGetPathPrefix(string? directory, out string? prefix)
    {
        prefix = null;
        if (string.IsNullOrWhiteSpace(directory) || HomeLocation.IsHome(directory))
        {
            return true;
        }

        try
        {
            var full = Path.GetFullPath(directory.Trim().Trim('"'));
            prefix = full.EndsWith('\\') ? full : full + "\\";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private SearchIndexStats RebuildCore(SearchIndexSettings settings, CancellationToken cancellationToken)
    {
        long files = 0;
        long folders = 0;
        long errors = 0;
        // Zero, not "now": the first directory is reported immediately, so the UI shows where the walk started
        // even when a small tree finishes inside the throttle window.
        long lastReport = 0;
        var tempPath = FilePath + ".rebuild-" + Guid.NewGuid().ToString("N") + ".db";
        try
        {
            using var connection = new SqliteConnection(BuildConnectionString(tempPath));
        connection.Open();
        PrepareBuildConnection(connection);
        EnsureSchema(connection);

        void Flush(List<(string Name, string Path, bool Directory)> buffer)
        {
            if (buffer.Count == 0)
            {
                return;
            }

            using var tx = connection.BeginTransaction();
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO file_name(name, path, is_dir) VALUES ($name, $path, $dir);";
            var name = insert.CreateParameter();
            name.ParameterName = "$name";
            insert.Parameters.Add(name);
            var path = insert.CreateParameter();
            path.ParameterName = "$path";
            insert.Parameters.Add(path);
            var dir = insert.CreateParameter();
            dir.ParameterName = "$dir";
            insert.Parameters.Add(dir);
            foreach (var row in buffer)
            {
                name.Value = row.Name;
                path.Value = row.Path;
                dir.Value = row.Directory ? "1" : "0";
                insert.ExecuteNonQuery();
            }

            tx.Commit();
            buffer.Clear();
        }

        void Report(string? current)
        {
            var previous = Volatile.Read(ref lastReport);
            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(0.2)
                || Interlocked.CompareExchange(ref lastReport, now, previous) != previous)
            {
                return;
            }

            Raise(new SearchIndexProgress(current, Volatile.Read(ref files), Volatile.Read(ref folders), Volatile.Read(ref errors), true));
        }

        // Walkers crawl the tree and hand finished batches to this thread, which is the only SQLite writer. The
        // enumeration already carries each entry's attributes, so no second stat call per file is needed.
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
        var rebuildPrefix = FilePath + ".rebuild-";
        using var batches = new System.Collections.Concurrent.BlockingCollection<List<(string Name, string Path, bool Directory)>>(boundedCapacity: 8);
        // Walkers stop on the caller's token, and also when the writer fails and nobody would drain their batches.
        using var walkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var walkToken = walkCancellation.Token;

        void Walk(string directory, int depth, ref List<(string Name, string Path, bool Directory)> buffer)
        {
            walkToken.ThrowIfCancellationRequested();
            if (SearchIndexPathRules.IsExcluded(directory, settings.Exclusions))
            {
                return;
            }

            Report(directory);
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref errors);
                return;
            }

            try
            {
                foreach (var entry in entries)
                {
                    walkToken.ThrowIfCancellationRequested();
                    var attributes = entry.Attributes;
                    var path = entry.FullName;
                    if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(rebuildPrefix, StringComparison.OrdinalIgnoreCase)
                        || (attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var name = entry.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var isDir = (attributes & FileAttributes.Directory) != 0;
                    if (isDir && SearchIndexPathRules.IsExcluded(path, settings.Exclusions))
                    {
                        continue;
                    }

                    buffer.Add((name, path, isDir));
                    if (isDir)
                    {
                        Interlocked.Increment(ref folders);
                    }
                    else
                    {
                        Interlocked.Increment(ref files);
                    }

                    if (buffer.Count >= 250)
                    {
                        batches.Add(buffer, walkToken);
                        buffer = new List<(string Name, string Path, bool Directory)>(256);
                    }

                    if (isDir && (settings.MaxDepth <= 0 || depth + 1 < settings.MaxDepth))
                    {
                        Walk(path, depth + 1, ref buffer);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The enumerator itself failed part-way (volume went away, directory deleted under us).
                Interlocked.Increment(ref errors);
            }
        }

        // One walker per root up to a small cap: independent volumes crawl concurrently, while a single busy
        // disk is not hammered by several heads. Sequential unless the user asked for parallel scanning.
        var roots = new System.Collections.Concurrent.ConcurrentQueue<string>(settings.Roots);
        var walkerCount = settings.ScanInParallel ? Math.Clamp(roots.Count, 1, 3) : 1;
        var remaining = walkerCount;
        Exception? walkerFailure = null;
        var walkers = new Thread[walkerCount];
        for (var i = 0; i < walkerCount; i++)
        {
            walkers[i] = new Thread(() =>
            {
                using var background = FilesMate.Platform.Windows.Threading.BackgroundThreadMode.Enter();
                var buffer = new List<(string Name, string Path, bool Directory)>(256);
                try
                {
                    while (!walkToken.IsCancellationRequested && roots.TryDequeue(out var root))
                    {
                        Walk(root, 0, ref buffer);
                        if (buffer.Count > 0)
                        {
                            batches.Add(buffer, walkToken);
                            buffer = new List<(string Name, string Path, bool Directory)>(256);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (InvalidOperationException)
                {
                    // The collection completed early because another walker failed; nothing more to hand over.
                }
                catch (Exception error)
                {
                    Interlocked.CompareExchange(ref walkerFailure, error, null);
                }
                finally
                {
                    if (Interlocked.Decrement(ref remaining) == 0) batches.CompleteAdding();
                }
            }) { IsBackground = true, Name = "FilesMate index walker", Priority = ThreadPriority.BelowNormal };
        }

        try
        {
            foreach (var walker in walkers) walker.Start();
            using (FilesMate.Platform.Windows.Threading.BackgroundThreadMode.Enter())
            {
                foreach (var batch in batches.GetConsumingEnumerable(cancellationToken))
                {
                    Flush(batch);
                }
            }
        }
        finally
        {
            // Whether we finished, were cancelled, or the writer failed: release any walker blocked on a full
            // queue and wait for all of them, so no thread outlives this rebuild or touches a disposed collection.
            walkCancellation.Cancel();
            foreach (var walker in walkers) walker.Join();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (walkerFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(walkerFailure);
        }

        NameIndexReader.BuildSubstringIndex(connection, cancellationToken);
        var completed = DateTimeOffset.UtcNow;
        using (var meta = connection.CreateCommand())
        {
            meta.CommandText = """
                INSERT INTO index_meta(key, value) VALUES ('files', $files)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                INSERT INTO index_meta(key, value) VALUES ('folders', $folders)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                INSERT INTO index_meta(key, value) VALUES ('errors', $errors)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                INSERT INTO index_meta(key, value) VALUES ('completed', $completed)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            meta.Parameters.AddWithValue("$files", files.ToString(CultureInfo.InvariantCulture));
            meta.Parameters.AddWithValue("$folders", folders.ToString(CultureInfo.InvariantCulture));
            meta.Parameters.AddWithValue("$errors", errors.ToString(CultureInfo.InvariantCulture));
            meta.Parameters.AddWithValue("$completed", completed.ToString("O", CultureInfo.InvariantCulture));
            meta.ExecuteNonQuery();
        }

        var stats = new SearchIndexStats(files, folders, errors, completed);
        connection.Close();
        cancellationToken.ThrowIfCancellationRequested();
        _gate.Wait(cancellationToken);
        try { CommitDatabase(tempPath); NeedsUpgrade = false; }
        finally { _gate.Release(); }
        tempPath = string.Empty;
        Raise(new SearchIndexProgress(null, files, folders, errors, false));
        return stats;
        }
        finally
        {
            DeleteDatabaseFiles(tempPath);
        }
    }

    private SearchIndexStats LoadStats()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            PrepareConnection(connection);
            EnsureSchema(connection);
            using var version = connection.CreateCommand();
            version.CommandText = "SELECT value FROM index_meta WHERE key='schema';";
            NeedsUpgrade = version.ExecuteScalar() as string != "2";
            return ReadStats(connection);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return SearchIndexStats.Empty;
        }
    }

    private static SearchIndexStats ReadStats(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM index_meta;";
        long files = 0;
        long folders = 0;
        long errors = 0;
        DateTimeOffset? completed = null;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            switch (key)
            {
                case "files":
                    _ = long.TryParse(value, CultureInfo.InvariantCulture, out files);
                    break;
                case "folders":
                    _ = long.TryParse(value, CultureInfo.InvariantCulture, out folders);
                    break;
                case "errors":
                    _ = long.TryParse(value, CultureInfo.InvariantCulture, out errors);
                    break;
                case "completed":
                    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        completed = parsed;
                    }

                    break;
            }
        }

        return new SearchIndexStats(files, folders, errors, completed);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
    }

    private static void PrepareConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=1000;";
        command.ExecuteNonQuery();
    }

    private static void PrepareBuildConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
        command.ExecuteNonQuery();
    }

    private static string BuildConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

    private void CommitDatabase(string tempPath)
    {
        if (!File.Exists(FilePath))
        {
            File.Move(tempPath, FilePath);
            return;
        }

        File.Replace(tempPath, FilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }

    private static void DeleteDatabaseFiles(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            try
            {
                File.Delete(path + suffix);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Raise(SearchIndexProgress progress) => ProgressChanged?.Invoke(this, progress);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS index_meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE VIRTUAL TABLE IF NOT EXISTS file_name USING fts5(name, path, is_dir UNINDEXED);
        """;
}
