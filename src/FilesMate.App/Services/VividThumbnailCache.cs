using System.IO;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Services;

/// <summary>Optional, read-only adapter for Vivid Player's local thumbnail index.</summary>
internal sealed class VividThumbnailCache
{
    private readonly object _sync = new();
    private readonly string _packageRoot;
    private readonly TimeProvider _time;
    private Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _reloadAt;

    public VividThumbnailCache(string? packageRoot = null, TimeProvider? time = null)
    {
        _packageRoot = packageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "43612cjw1115.VividPlayer_6ey2ky5vtg2yj");
        _time = time ?? TimeProvider.System;
    }

    // Called only from the thumbnail worker, never from element preparation.
    public string? Find(string videoPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Entry? entry;
        lock (_sync)
        {
            if (_time.GetUtcNow() >= _reloadAt)
                Reload(token);
            _entries.TryGetValue(videoPath, out entry);
        }
        if (entry is null)
            return null;

        try
        {
            token.ThrowIfCancellationRequested();
            var video = new FileInfo(videoPath);
            if (!video.Exists || video.Length != entry.Size
                || new DateTimeOffset(video.LastWriteTimeUtc).ToUnixTimeSeconds() != entry.Modified)
                return null;
            return File.Exists(entry.Thumbnail) ? entry.Thumbnail : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _reloadAt = default;
        }
    }

    private void Reload(CancellationToken token)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var database = Path.Combine(_packageRoot, "LocalState", "library.db");
        try
        {
            if (File.Exists(database))
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = database,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false,
                    DefaultTimeout = 1,
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT path, thumb_path, size_bytes, mtime
                    FROM library_items
                    WHERE thumb_path IS NOT NULL AND thumb_path != ''
                        AND size_bytes IS NOT NULL AND mtime IS NOT NULL
                    LIMIT 20000
                    """;
                using var reader = command.ExecuteReader();
                var cacheRoot = Path.GetFullPath(Path.Combine(_packageRoot, "LocalCache")) + Path.DirectorySeparatorChar;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    var thumbnail = Path.GetFullPath(reader.GetString(1));
                    if (thumbnail.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Path.GetExtension(thumbnail), ".jpg", StringComparison.OrdinalIgnoreCase))
                        entries[reader.GetString(0)] = new(thumbnail, reader.GetInt64(2), reader.GetInt64(3));
                }
            }
        }
        catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Missing app, locked database or a future incompatible schema uses Windows instead.
            entries.Clear();
        }
        _entries = entries;
        _reloadAt = _time.GetUtcNow().AddSeconds(entries.Count == 0 ? 5 : 30);
    }

    private sealed record Entry(string Thumbnail, long Size, long Modified);
}
