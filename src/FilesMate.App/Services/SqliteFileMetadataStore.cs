using FilesMate.App.Models;
using FilesMate.Core.Metadata;

using Microsoft.Data.Sqlite;

namespace FilesMate.App.Services;

public sealed class SqliteFileMetadataStore : IFileMetadataStore
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilesMate",
        "metadata.db");

    private const int CurrentSchemaVersion = 1;
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

        var volume = reader.IsDBNull(2) ? (ulong?)null : checked((ulong)reader.GetInt64(2));
        var fileId = reader.IsDBNull(3) ? (ulong?)null : checked((ulong)reader.GetInt64(3));
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

            try
            {
                await CreateSchemaAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                PreserveCorruptDatabase();
                await CreateSchemaAsync(cancellationToken).ConfigureAwait(false);
            }

            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

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

    private void PreserveCorruptDatabase()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var preserved = _filePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        File.Move(_filePath, preserved, overwrite: false);
        foreach (var sidecar in new[] { _filePath + "-wal", _filePath + "-shm" })
        {
            if (File.Exists(sidecar))
            {
                File.Move(sidecar, preserved + Path.GetExtension(sidecar), overwrite: true);
            }
        }
    }

    private static void AddIdentityParameters(SqliteCommand command, FileIdentity identity)
    {
        command.Parameters.AddWithValue("$key", identity.StableKey);
        command.Parameters.AddWithValue("$path", identity.NormalizedPath);
        command.Parameters.AddWithValue("$volume", identity.VolumeSerial.HasValue ? checked((long)identity.VolumeSerial.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$fileId", identity.FileId.HasValue ? checked((long)identity.FileId.Value) : DBNull.Value);
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
