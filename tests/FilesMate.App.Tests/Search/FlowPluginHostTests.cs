using FilesMate.FlowPlugin;

namespace FilesMate.App.Tests.Search;

public sealed class FlowPluginHostTests
{
    [Fact]
    public void Empty_query_returns_no_hits()
    {
        var json = FlowPluginHost.Handle("""{"method":"query","parameters":[""]}""");
        Assert.Contains("\"Result\":[]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_index_explains_how_to_build_it()
    {
        var json = FlowPluginHost.Handle(
            """{"method":"query","parameters":["chrome"]}""",
            databasePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db"));
        using var response = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("FilesMate 尚未建立搜索索引", response.RootElement.GetProperty("Result")[0].GetProperty("Title").GetString());
        Assert.DoesNotContain("Chrome.exe", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Indexed_names_are_returned_as_flow_results()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var db = Path.Combine(root, "index.db");
        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                       new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                       {
                           DataSource = db,
                           Pooling = false,
                       }.ToString()))
            {
                connection.Open();
                using var schema = connection.CreateCommand();
                schema.CommandText = "CREATE VIRTUAL TABLE file_name USING fts5(name, path, is_dir UNINDEXED);";
                schema.ExecuteNonQuery();
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO file_name(name, path, is_dir) VALUES ('Chrome.exe', 'C:\\Chrome.exe', '0'), ('Notes.txt', 'C:\\Notes.txt', '0');";
                insert.ExecuteNonQuery();
            }

            var json = FlowPluginHost.Handle("""{"method":"query","parameters":["chrome"]}""", db);
            Assert.Contains("Chrome.exe", json, StringComparison.Ordinal);
            Assert.Contains("\"Method\":\"open\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Notes.txt", json, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plugin_manifest_is_an_executable_flow_plugin()
    {
        var manifest = File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.RepoRoot,
            "src",
            "FilesMate.FlowPlugin",
            "plugin.json"));
        Assert.Contains("\"Language\": \"executable\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"ActionKeyword\": \"fm\"", manifest, StringComparison.Ordinal);
        Assert.Contains("FilesMate.FlowPlugin.exe", manifest, StringComparison.Ordinal);
    }
}
