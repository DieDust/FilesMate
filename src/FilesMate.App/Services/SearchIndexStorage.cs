using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

namespace FilesMate.App.Services;

public sealed record SearchIndexUsage(long Bytes, bool IsAvailable);
public sealed record SearchIndexMigrationResult(string DatabasePath, bool OldFilesRetained, string? RetentionReason);

/// <summary>Accounts for SQLite's complete file set and publishes consistent index snapshots.</summary>
public static class SearchIndexStorage
{
    private static readonly string[] Suffixes = ["", "-wal", "-shm", "-journal"];

    public static SearchIndexUsage ReadUsage(string databasePath, string? buildingPath = null)
    {
        long bytes = 0;
        var available = true;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { databasePath, buildingPath })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var suffix in Suffixes)
            {
                try
                {
                    var path = Path.GetFullPath(root) + suffix;
                    if (!paths.Add(path)) continue;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) { available = false; continue; }
                    bytes = checked(bytes + new FileInfo(path).Length);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException)
                { available = false; }
            }
        }
        return new(bytes, available);
    }

    public static void ValidateDatabase(string path, bool checkIntegrity = false)
    {
        RequireRegularFile(path);
        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        Validate(connection, checkIntegrity);
    }

    public static async Task<SearchIndexMigrationResult> MigrateAsync(string source, string destination,
        Func<string, CancellationToken, Task> publishSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishSettings);
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The index destination is the current location.");
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDestinationAvailable(destination);
        var sourceExists = EntryExists(source);
        if (sourceExists) ValidateDatabase(source);
        else if (Suffixes.Skip(1).Any(suffix => EntryExists(source + suffix)))
            throw new IOException("The index database is missing but its journal files remain.");

        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".filesmate-index-migration-" + Guid.NewGuid().ToString("N") + ".db");
        FileStream? sourceGuard = null;
        Fingerprint? sourceIdentity = null;
        Fingerprint? createdIdentity = null;
        var published = false;
        var settingsPublished = false;
        try
        {
            // An unshared, journal-free source can be removed after committing settings.
            // A WAL connection may still be in use by SearchHost; retain that whole set.
            if (sourceExists && !HasSidecars(source))
            {
                try
                {
                    sourceGuard = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sourceIdentity = Identity(sourceGuard.SafeFileHandle);
                    if (HasSidecars(source)) { sourceGuard.Dispose(); sourceGuard = null; sourceIdentity = null; }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { sourceGuard?.Dispose(); sourceGuard = null; }
            }
            // Reserve only a new, unpredictable file. BackupDatabase reads committed WAL
            // state; a raw File.Copy would silently lose recently committed index rows.
            using (var reservation = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                createdIdentity = Identity(reservation.SafeFileHandle);
                try { await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var output = Open(staging, SqliteOpenMode.ReadWrite);
                    if (sourceExists)
                    {
                        using var input = Open(source, SqliteOpenMode.ReadOnly);
                        Validate(input, false);
                        using var interrupt = cancellationToken.Register(() =>
                        {
                            SQLitePCL.raw.sqlite3_interrupt(input.Handle);
                            SQLitePCL.raw.sqlite3_interrupt(output.Handle);
                        });
                        try { input.BackupDatabase(output); }
                        catch (SqliteException) when (cancellationToken.IsCancellationRequested)
                        { throw new OperationCanceledException(cancellationToken); }
                    }
                    else
                    {
                        Execute(output, "CREATE TABLE index_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE VIRTUAL TABLE file_name USING fts5(name,path,is_dir UNINDEXED);");
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    Execute(output, "PRAGMA journal_mode=DELETE;");
                    Validate(output, true);
                }, cancellationToken).ConfigureAwait(false); }
                finally { createdIdentity = Identity(reservation.SafeFileHandle); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDestinationAvailable(destination);
            File.Move(staging, destination, overwrite: false);
            published = true;
            using (var destinationGuard = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (createdIdentity is { } created && Identity(destinationGuard.SafeFileHandle) != created)
                    throw new IOException("The migrated index was replaced before settings could be published.");
                if (HasSidecars(destination)) throw new IOException("Index journal files appeared at the destination.");
                cancellationToken.ThrowIfCancellationRequested();
                // This callback must atomically publish settings or throw without changing them.
                // Once it succeeds the destination belongs to the application, even if cancellation races it.
                await publishSettings(destination, cancellationToken).ConfigureAwait(false);
                settingsPublished = true;
            }
            sourceGuard?.Dispose();
            sourceGuard = null;
            var removed = !sourceExists || sourceIdentity is { } expected && !HasSidecars(source) && DeleteSameFile(source, expected);
            return new(destination, !removed, removed ? null : "SourceRetainedForSafety");
        }
        finally
        {
            sourceGuard?.Dispose();
            if (!settingsPublished && createdIdentity is { } expected)
                DeleteSameFile(published ? destination : staging, expected);
            // Never delete source journals or paths selected only by a familiar suffix.
        }
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 2 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static void Validate(SqliteConnection connection, bool integrity)
    {
        // No CREATE/ALTER/INSERT or extension loading is allowed on a selected source.
        using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT type,sql FROM sqlite_master WHERE name=$name;";
        schema.Parameters.AddWithValue("$name", "index_meta");
        using (var reader = schema.ExecuteReader())
            if (!reader.Read() || reader.GetString(0) != "table" || reader.IsDBNull(1)
                || reader.GetString(1).Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This database is not a FilesMate search index.");
        CheckColumns(connection, "index_meta", ["key", "value"]);
        CheckFts(connection, "file_name", ["name", "path", "is_dir"]);
        using var version = connection.CreateCommand();
        version.CommandText = "SELECT value FROM index_meta WHERE key='schema';";
        var value = version.ExecuteScalar() as string;
        if (value is not null and not ("1" or "2" or "3"))
            throw new InvalidDataException("The search index schema is unsupported.");
        if (value == "2") CheckFts(connection, "file_name_substring", ["name"]);
        if (value == "3") CheckFts(connection, "file_name_gram", ["grams"]);
        if (integrity)
        {
            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            using var results = check.ExecuteReader();
            if (!results.Read() || results.GetString(0) != "ok" || results.Read())
                throw new InvalidDataException("The search index failed its integrity check.");
        }
    }

    private static void CheckFts(SqliteConnection connection, string table, string[] columns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        var sql = command.ExecuteScalar() as string;
        if (sql is null || !System.Text.RegularExpressions.Regex.IsMatch(sql, @"\bUSING\s+fts5\s*\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidDataException("The search index is missing its filename search table.");
        CheckColumns(connection, table, columns);
    }

    private static void CheckColumns(SqliteConnection connection, string table, string[] expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});"; // Table is one of the fixed names above.
        using var reader = command.ExecuteReader();
        var actual = new List<string>();
        while (reader.Read()) actual.Add(reader.GetString(1));
        if (!actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The search index table layout is unsupported.");
    }

    private static void Execute(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static bool HasSidecars(string path) => Suffixes.Skip(1).Any(suffix => EntryExists(path + suffix));
    private static void EnsureDestinationAvailable(string path)
    {
        if (Suffixes.Any(suffix => EntryExists(path + suffix)))
            throw new IOException("The destination already contains a database, journal, or directory.");
    }
    private static bool EntryExists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static void RequireRegularFile(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("The search index must be a regular database file.");
    }

    private readonly record struct Fingerprint(uint Volume, ulong Id, ulong Bytes, long Written);
    private static Fingerprint? Identity(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows() || !GetFileInformationByHandle(handle, out var info)
            || info.Volume == 0 || (info.IdHigh == 0 && info.IdLow == 0) || (info.Attributes & 0x410) != 0) return null;
        return new(info.Volume, ((ulong)info.IdHigh << 32) | info.IdLow, ((ulong)info.SizeHigh << 32) | info.SizeLow,
            ((long)info.Written.High << 32) | info.Written.Low);
    }
    private static bool DeleteSameFile(string path, Fingerprint expected)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            // Open the object for deletion, deny concurrent writes/renames, validate
            // the handle itself, and delete that object without resolving the path again.
            using var handle = CreateFileW(path, 0x80010000, FileShare.Read, 0, 3, 0x00200000, 0);
            if (handle.IsInvalid || Identity(handle) != expected) return false;
            var delete = new Disposition { Delete = true };
            return SetFileInformationByHandle(handle, 4, ref delete, (uint)Marshal.SizeOf<Disposition>());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct Information
    { public uint Attributes; public FileTime Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow; }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.Bool)] public bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information information);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref Disposition information, uint length);
}
