using FilesMate.App.Services;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Icons;

public sealed class VividThumbnailCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-Vivid-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Existing_matching_thumbnail_is_reused_without_changing_database()
    {
        var (video, thumb, database) = Seed();
        var before = File.ReadAllBytes(database);
        Assert.Equal(thumb, new VividThumbnailCache(_root).Find(video, default));
        Assert.Equal(before, File.ReadAllBytes(database));
    }

    [Fact]
    public void Replaced_video_and_deleted_thumbnail_fall_back()
    {
        var (video, thumb, _) = Seed();
        var cache = new VividThumbnailCache(_root);
        Assert.Equal(thumb, cache.Find(video, default));
        File.AppendAllText(video, "changed");
        Assert.Null(cache.Find(video, default));
        File.WriteAllText(video, "video");
        File.Delete(thumb);
        Assert.Null(cache.Find(video, default));
    }

    [Fact]
    public void Missing_app_does_not_create_database()
    {
        Assert.Null(new VividThumbnailCache(_root).Find("missing.mp4", default));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Unknown_database_schema_falls_back()
    {
        Directory.CreateDirectory(Path.Combine(_root, "LocalState"));
        using (var connection = new SqliteConnection("Data Source=" + Path.Combine(_root, "LocalState", "library.db")))
            connection.Open();
        Assert.Null(new VividThumbnailCache(_root).Find("missing.mp4", default));
    }

    [Fact]
    public void Paths_outside_player_cache_are_not_used()
    {
        var (video, _, database) = Seed();
        using (var connection = new SqliteConnection("Data Source=" + database))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE library_items SET thumb_path = $path";
            command.Parameters.AddWithValue("$path", Path.Combine(_root, "outside.jpg"));
            command.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(_root, "outside.jpg"), "image");
        Assert.Null(new VividThumbnailCache(_root).Find(video, default));
    }

    private (string Video, string Thumb, string Database) Seed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "LocalState"));
        Directory.CreateDirectory(Path.Combine(_root, "LocalCache", "thumbs"));
        var video = Path.Combine(_root, "video.mp4");
        var thumb = Path.Combine(_root, "LocalCache", "thumbs", "cover.jpg");
        var database = Path.Combine(_root, "LocalState", "library.db");
        File.WriteAllText(video, "video");
        File.WriteAllText(thumb, "image");
        using var connection = new SqliteConnection("Data Source=" + database + ";Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE library_items(path TEXT, thumb_path TEXT, size_bytes INTEGER, mtime INTEGER);
            INSERT INTO library_items VALUES($video, $thumb, $size, $mtime);
            """;
        command.Parameters.AddWithValue("$video", video);
        command.Parameters.AddWithValue("$thumb", thumb);
        var info = new FileInfo(video);
        command.Parameters.AddWithValue("$size", info.Length);
        command.Parameters.AddWithValue("$mtime", new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds());
        command.ExecuteNonQuery();
        return (video, thumb, database);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
