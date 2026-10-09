using System.Buffers.Binary;
using FilesMate.App.Models;
using FilesMate.Core.Metadata;

using Microsoft.Data.Sqlite;

namespace FilesMate.App.Services;

public sealed class SqliteFileMetadataStore : IFileMetadataStore
{
    public static string DefaultFilePath => Path.Combine(FilesMate.Core.Configuration.RuntimeProfile.DirectoryPath,
        "metadata.db");

    private const int CurrentSchemaVersion = 1;
    // SQLite sharing and recovery ownership apply to the database, not to one
    // store instance. Wait before opening a connection so a competing initializer
    // cannot keep the corrupt object alive while another tries to preserve it.
    private static readonly Dictionary<string, SchemaInitializationEntry> SchemaInitializations = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _filePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaReady;
    private bool _disposed;

    public SqliteFileMetadataStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Metadata database path is required.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    public async Task<IReadOnlyList<TagDefinition>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, color, sort_order FROM tag ORDER BY sort_order, id;";
        var result = new List<TagDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new TagDefinition(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        }

        return result;
    }

    public async Task<TagDefinition> CreateTagAsync(
        string name,
        string color,
        int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        name = NormalizeTagName(name);
        color = NormalizeColor(color);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tag(name, color, sort_order)
            VALUES ($name, $color, COALESCE($sortOrder, (SELECT COALESCE(MAX(sort_order) + 1, 0) FROM tag)));
            SELECT id, name, color, sort_order FROM tag WHERE id = last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$color", color);
        command.Parameters.AddWithValue("$sortOrder", (object?)sortOrder ?? DBNull.Value);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The tag was inserted but could not be read back.");
            }

            return new TagDefinition(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"A tag named '{name}' already exists.", ex);
        }
    }

    public async Task DeleteTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        if (tagId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagId));
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tag WHERE id = $id;";
        command.Parameters.AddWithValue("$id", tagId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TagDefinition> UpdateTagAsync(
        long tagId,
        string name,
        string color,
        CancellationToken cancellationToken = default)
    {
        if (tagId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagId));
        }

        name = NormalizeTagName(name);
        color = NormalizeColor(color);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tag
            SET name = $name, color = $color
            WHERE id = $id
            RETURNING id, name, color, sort_order;
            """;
        command.Parameters.AddWithValue("$id", tagId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$color", color);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The tag no longer exists.");
            }

            return new TagDefinition(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"A tag named '{name}' already exists.", ex);
        }
    }

    public async Task ReorderTagsAsync(IReadOnlyList<long> orderedIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (orderedIds.Any(id => id <= 0))
        {
            throw new ArgumentException("Tag ids must be positive.", nameof(orderedIds));
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var order = 0;
        foreach (var tagId in orderedIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE tag SET sort_order = $order WHERE id = $id;";
            command.Parameters.AddWithValue("$order", order);
            command.Parameters.AddWithValue("$id", tagId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            order++;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertFileIdentityAsync(FileIdentity identity, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO file_identity(identity_key, normalized_path, volume_serial, file_id, updated_utc)
            VALUES ($key, $path, $volume, $fileId, $updated)
            ON CONFLICT(identity_key) DO UPDATE SET
                normalized_path = excluded.normalized_path,
                volume_serial = excluded.volume_serial,
                file_id = excluded.file_id,
                updated_utc = excluded.updated_utc;
            """;
        AddIdentityParameters(command, identity);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileIdentity?> GetIdentityAsync(string stableKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stableKey))
        {
            throw new ArgumentException("Stable key is required.", nameof(stableKey));
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT identity_key, normalized_path, volume_serial, file_id FROM file_identity WHERE identity_key = $key;";
        command.Parameters.AddWithValue("$key", stableKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // SQLite INTEGER is signed; preserve the unsigned identity's complete bit
        // pattern. Existing positive values and stable-key strings stay unchanged.
        var volume = reader.IsDBNull(2) ? (ulong?)null : unchecked((ulong)reader.GetInt64(2));
        var fileId = reader.IsDBNull(3) ? (ulong?)null : unchecked((ulong)reader.GetInt64(3));
        return volume.HasValue && fileId.HasValue
            ? FileIdentity.FromStable(volume.Value, fileId.Value, reader.GetString(1))
            : FileIdentity.FromNormalizedPath(reader.GetString(1));
    }

    public async Task<IReadOnlyList<TagDefinition>> GetTagsAsync(
        FileIdentity identity,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.name, t.color, t.sort_order
            FROM tag t
            INNER JOIN file_tag ft ON ft.tag_id = t.id
            WHERE ft.identity_key = $key
            ORDER BY t.sort_order, t.id;
            """;
        command.Parameters.AddWithValue("$key", identity.StableKey);
        var result = new List<TagDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new TagDefinition(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        }

        return result;
    }

    public async Task SetTagsAsync(
        FileIdentity identity,
        IReadOnlyCollection<long> tagIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagIds);
        if (tagIds.Any(id => id <= 0))
        {
            throw new ArgumentException("Tag ids must be positive.", nameof(tagIds));
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var identityCommand = connection.CreateCommand())
        {
            identityCommand.Transaction = transaction;
            identityCommand.CommandText = """
                INSERT INTO file_identity(identity_key, normalized_path, volume_serial, file_id, updated_utc)
                VALUES ($key, $path, $volume, $fileId, $updated)
                ON CONFLICT(identity_key) DO UPDATE SET normalized_path = excluded.normalized_path, updated_utc = excluded.updated_utc;
                """;
            AddIdentityParameters(identityCommand, identity);
            await identityCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM file_tag WHERE identity_key = $key;";
            deleteCommand.Parameters.AddWithValue("$key", identity.StableKey);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var tagId in tagIds.Distinct())
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO file_tag(identity_key, tag_id) VALUES ($key, $tagId);";
            insertCommand.Parameters.AddWithValue("$key", identity.StableKey);
            insertCommand.Parameters.AddWithValue("$tagId", tagId);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListPathsForTagAsync(
        long tagId,
        CancellationToken cancellationToken = default)
    {
        if (tagId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagId));
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT fi.normalized_path
            FROM file_tag ft
            INNER JOIN file_identity fi ON fi.identity_key = ft.identity_key
            WHERE ft.tag_id = $id
            ORDER BY fi.normalized_path COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$id", tagId);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var path = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    public async Task<string> GetJournalModeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _schemaGate.WaitAsync().ConfigureAwait(false);
        _schemaGate.Release();
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_schemaReady)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            using var initialization = await AcquireSchemaInitializationAsync(_filePath, cancellationToken).ConfigureAwait(false);
            // An interrupted recovery must never turn orphaned journals into a new,
            // empty metadata database. Keep the complete set for manual recovery.
            var hadRecoverySidecars = HasRecoverySidecars(_filePath);
            if (hadRecoverySidecars && (!EntryExists(_filePath) || !HasSqliteHeader(_filePath)))
                throw new IOException("The metadata database is missing or unreadable while its journal files remain; the original files were retained.");
            if (hadRecoverySidecars)
            {
                ValidateWalHeader(_filePath);
                await ValidateRecoveryInputAsync(cancellationToken).ConfigureAwait(false);
            }
            try
            {
                await CreateSchemaAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException error) when (IsCorruption(error))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Busy, read-only, full-disk and schema errors are not corruption.
                // If ownership of a corrupt file is uncertain, preserve the original
                // exception and every original path rather than silently resetting tags.
                // Opening a malformed file can make SQLite remove an invalid WAL.
                // That must not turn uncertain multi-file input into a reset candidate.
                if (hadRecoverySidecars || !TryPreserveCorruptDatabase(_filePath)) throw;
                await CreateSchemaAsync(cancellationToken).ConfigureAwait(false);
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private static async Task<SchemaInitializationLease> AcquireSchemaInitializationAsync(
        string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SchemaInitializationEntry entry;
        lock (SchemaInitializations)
        {
            if (!SchemaInitializations.TryGetValue(path, out entry!))
                SchemaInitializations.Add(path, entry = new SchemaInitializationEntry());
            entry.Users++;
        }

        var lease = new SchemaInitializationLease(path, entry);
        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            lease.Acquired = true;
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private sealed class SchemaInitializationEntry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Users;
    }

    private sealed class SchemaInitializationLease(string path, SchemaInitializationEntry entry) : IDisposable
    {
        private SchemaInitializationEntry? _entry = entry;
        public bool Acquired { get; set; }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _entry, null);
            if (current is null) return;
            if (Acquired) current.Gate.Release();
            lock (SchemaInitializations)
            {
                // Register before waiting, and remove only after the last holder
                // or waiter releases its lease. No idle database paths accumulate.
                if (--current.Users == 0)
                {
                    SchemaInitializations.Remove(path);
                    current.Gate.Dispose();
                }
            }
        }
    }

    private static void ValidateWalHeader(string path)
    {
        try
        {
            using var wal = new FileStream(path + "-wal", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (wal.Length == 0) return; // A legitimate empty/new WAL carries no frames.
            Span<byte> header = stackalloc byte[32];
            if (wal.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
                throw new IOException("The metadata WAL header is incomplete; original files were retained. Retry after the writer finishes.");
            var magic = BinaryPrimitives.ReadUInt32BigEndian(header);
            var version = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            var pageSize = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            // SQLite can silently ignore an invalid WAL and quick_check may still
            // report a healthy main file. Reject clear header damage before open.
            // This is not a WAL frame/checksum parser or a recovery implementation.
            if (magic is not (0x377f0682 or 0x377f0683) || version != 3007000
                || pageSize is < 512 or > 65536 || (pageSize & (pageSize - 1)) != 0)
                throw new IOException("The metadata WAL header is invalid or unsupported; original files were retained for recovery.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private Task ValidateRecoveryInputAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        // Validate through SQLite's read-only WAL-aware view before writable open
        // can discard an invalid journal. SHM is SQLite's derived coordination
        // state and may be maintained; main/WAL/rollback recovery bytes stay read-only.
        // Run the integrity scan off the caller's UI thread.
        var settings = new SqliteConnectionStringBuilder(_connectionString)
        { Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private };
        await using var connection = new SqliteConnection(settings.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var interrupt = cancellationToken.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA quick_check(1);";
        object? result;
        try { result = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false); }
        catch (SqliteException) when (cancellationToken.IsCancellationRequested)
        { throw new OperationCanceledException(cancellationToken); }
        cancellationToken.ThrowIfCancellationRequested();
        if (result is not "ok")
            throw new InvalidDataException("The metadata database needs recovery; original files were retained. " + result);
    }, cancellationToken);

    private async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            await setup.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tag (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                color TEXT NOT NULL,
                sort_order INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS file_identity (
                identity_key TEXT PRIMARY KEY,
                normalized_path TEXT NOT NULL,
                volume_serial INTEGER NULL,
                file_id INTEGER NULL,
                updated_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS file_tag (
                identity_key TEXT NOT NULL REFERENCES file_identity(identity_key) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tag(id) ON DELETE CASCADE,
                PRIMARY KEY(identity_key, tag_id)
            );
            CREATE INDEX IF NOT EXISTS ix_file_tag_tag_id ON file_tag(tag_id);
            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static bool IsCorruption(SqliteException error) => error.SqliteErrorCode is 11 or 26;

    internal static bool TryPreserveCorruptDatabase(string path)
    {
        try
        {
            // SQLite's hot journals are part of the database. Their presence can
            // also mean another connection owns it. A multi-file rename is not
            // atomic: leave the whole set, with its exact suffixes, in place.
            if (HasRecoverySidecars(path)) return false;
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
            // On Windows, deny other read/write owners while permitting our one
            // atomic rename. An active SQLite connection makes this guard fail.
            using var guard = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            // Recheck the object we now own. Another initializer may have replaced
            // the earlier corrupt file with a healthy database before this guard.
            // An empty replacement is valid SQLite initialization input, not corruption.
            // Recognized SQLite files also deserve deeper/manual recovery, not reset.
            if (guard.Length == 0 || HasSqliteHeader(guard) || HasRecoverySidecars(path)) return false;
            var preserved = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff")
                + "-" + Guid.NewGuid().ToString("N");
            File.Move(path, preserved, overwrite: false);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Metadata recovery left the original file in place: {0}", error);
            return false;
        }
    }

    private static bool HasSqliteHeader(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return HasSqliteHeader(file);
    }

    private static bool HasSqliteHeader(Stream file)
    {
        Span<byte> header = stackalloc byte[16];
        return file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
            && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private static bool HasRecoverySidecars(string path) =>
        EntryExists(path + "-wal") || EntryExists(path + "-shm") || EntryExists(path + "-journal");

    private static bool EntryExists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void AddIdentityParameters(SqliteCommand command, FileIdentity identity)
    {
        command.Parameters.AddWithValue("$key", identity.StableKey);
        command.Parameters.AddWithValue("$path", identity.NormalizedPath);
        command.Parameters.AddWithValue("$volume", identity.VolumeSerial.HasValue ? unchecked((long)identity.VolumeSerial.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$fileId", identity.FileId.HasValue ? unchecked((long)identity.FileId.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
    }

    private static string NormalizeTagName(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64 || name.Contains('\0'))
        {
            throw new ArgumentException("Tag name must contain 1 to 64 characters.", nameof(name));
        }

        return name;
    }

    private static string NormalizeColor(string color)
    {
        color = color?.Trim() ?? string.Empty;
        if (color.Length != 7 || color[0] != '#' || !color[1..].All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Tag color must be a #RRGGBB value.", nameof(color));
        }

        return color.ToUpperInvariant();
    }
}
