using FilesMate.App.Models;
using FilesMate.App.Services;
using FilesMate.FlowPlugin;
using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Search;

public sealed class SearchRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.SearchRegression", Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(_root, "index.db");

    public SearchRegressionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void App_and_plugin_match_filename_substrings_without_matching_parent_names(bool upgraded)
    {
        CreateIndex(upgraded,
            ("MyAetherSwap.exe", @"C:\Programs\MyAetherSwap.exe"),
            ("readme.txt", @"C:\AetherSwap\readme.txt"),
            ("中文测试文档.txt", @"C:\中文测试文档.txt"),
            ("report_100%.txt", @"C:\report_100%.txt"),
            ("reportA100X.txt", @"C:\reportA100X.txt"));
        Assert.Single(NameIndexReader.Search(Database, "aether", null, 40));
        Assert.Single(FlowPluginHost.Query("aether", Database));
        Assert.Equal("MyAetherSwap.exe", FlowPluginHost.Query("swap exe", Database).Single().Title);
        Assert.Equal("中文测试文档.txt", NameIndexReader.Search(Database, "文档", null, 40).Single().Name);
        Assert.Equal("report_100%.txt", NameIndexReader.Search(Database, "_100%", null, 40).Single().Name);
        Assert.Empty(NameIndexReader.Search(Database, "missing", null, 40));
    }

    [Fact]
    public void Exact_result_is_ranked_before_the_candidate_limit()
    {
        var rows = Enumerable.Range(0, 300).Select(i => ($"long-aether-other-{i}.txt", $@"C:\long-aether-other-{i}.txt"))
            .Append(("aether.exe", @"C:\aether.exe")).ToArray();
        CreateIndex(true, rows);
        Assert.Equal("aether.exe", NameIndexReader.Search(Database, "aether", null, 40)[0].Name);
        Assert.Equal("aether.exe", FlowPluginHost.Query("aether", Database)[0].Title);
    }

    [Theory]
    [InlineData("a", 4000)]
    [InlineData("abc", 20000)]
    [InlineData("文档", 5000)]
    public void Exact_match_inserted_after_many_substrings_is_not_lost(string query, int count)
    {
        CreateIndex(true, Enumerable.Range(0, count)
            .Select(i => ($"{query}_long_name_{i}.txt", $@"C:\{query}_long_name_{i}.txt"))
            .Append(($"{query}.txt", $@"C:\{query}.txt")).ToArray());
        Assert.Equal(query + ".txt", NameIndexReader.Search(Database, query, null, 80)[0].Name);
        var page1 = NameIndexReader.Search(Database, query, null, 20);
        var page2 = NameIndexReader.Search(Database, query, null, 20, offset: 20);
        Assert.Empty(page1.Select(h => h.Path).Intersect(page2.Select(h => h.Path)));
    }

    [Fact]
    public void Short_absent_query_uses_postings_instead_of_evaluating_every_row()
    {
        CreateIndex(true, Enumerable.Range(0, 100000).Select(i => ($"file_{i}.txt", $@"C:\file_{i}.txt")).ToArray());
        var diagnostics = new NameSearchDiagnostics();
        Assert.Empty(NameIndexReader.Search(Database, "zz", null, 80, diagnostics: diagnostics));
        Assert.Equal(0, diagnostics.EvaluatedNames);
    }

    [Theory]
    [InlineData("ÉTÉ", "été.txt")]
    [InlineData("文档", "中文文档.txt")]
    [InlineData("a%", "A%report.txt")]
    [InlineData("𐐀", "𐐨.txt")]
    public void Encoded_index_preserves_ordinal_case_and_literal_characters(string query, string name)
    {
        CreateIndex(true, (name, @"C:\" + name));
        Assert.Equal(name, Assert.Single(NameIndexReader.Search(Database, query, null, 40)).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Flow_prioritizes_applications_before_limiting_candidates(bool upgraded)
    {
        var rows = Enumerable.Range(0, 100)
            .Select(i => ("aether", $@"C:\config{i}\aether", true))
            .Append(("MyAetherTool.exe", @"C:\Apps\MyAetherTool.exe", false))
            .Append(("AetherSwap.EXE", @"C:\Apps\AetherSwap.EXE", false))
            .Append(("aether.com", @"C:\Apps\aether.com", false))
            .ToArray();
        CreateIndex(upgraded, rows);

        var results = FlowPluginHost.Query("aether", Database);
        Assert.Equal(40, results.Count);
        Assert.Equal(new[] { "aether.com", "AetherSwap.EXE", "MyAetherTool.exe" }, results.Take(3).Select(r => r.Title));
        Assert.All(results.Skip(3), result => Assert.Equal("aether", result.Title));
        Assert.True(results[2].Score > results[3].Score);
        Assert.Equal(results.OrderByDescending(r => r.Score), results);
        Assert.Equal("open", results[0].JsonRPCAction!.Method);
        Assert.Equal(new object[] { @"C:\Apps\aether.com" }, results[0].JsonRPCAction!.Parameters);
        // The file manager retains its own filename relevance order.
        Assert.Equal("aether", NameIndexReader.Search(Database, "aether", null, 40)[0].Name);
    }

    [Fact]
    public void Flow_does_not_boost_documents_shortcuts_or_directories_named_like_executables()
    {
        CreateIndex(true,
            ("aether.exe", @"C:\folders\aether.exe", true),
            ("aether.exe.txt", @"C:\docs\aether.exe.txt", false),
            ("aether.dll", @"C:\docs\aether.dll", false),
            ("aether.lnk", @"C:\docs\aether.lnk", false),
            ("MyAetherApp.exe", @"C:\Apps\MyAetherApp.exe", false));
        var results = FlowPluginHost.Query("aether", Database);
        Assert.Equal("MyAetherApp.exe", results[0].Title);
        Assert.All(results.Skip(1), result => Assert.InRange(result.Score, 55, 85));
        Assert.Equal(results.OrderByDescending(r => r.Score), results);
    }

    [Fact]
    public async Task Current_folder_finds_new_deep_files_even_with_an_existing_unrelated_index()
    {
        var indexed = Directory.CreateDirectory(Path.Combine(_root, "indexed")).FullName;
        File.WriteAllText(Path.Combine(indexed, "existing.txt"), "");
        await using var index = new FileNameIndexService(Database);
        await index.RebuildAsync(SearchIndexSettings.Sanitize([indexed], [], false));
        var current = Path.Combine(_root, "unindexed");
        var deep = Directory.CreateDirectory(Path.Combine(current, "a", "b", "c", "d", "e")).FullName;
        var target = Path.Combine(deep, "FreshAetherFile.txt");
        File.WriteAllText(target, "");
        Assert.Contains(await index.SearchAsync("aether", current), hit => hit.Path == target);
    }

    [Fact]
    public async Task Old_index_remains_searchable_while_rebuild_is_paused()
    {
        var content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
        File.WriteAllText(Path.Combine(content, "kept.txt"), "");
        await using var index = new FileNameIndexService(Database);
        var settings = SearchIndexSettings.Sanitize([content], [], false);
        await index.RebuildAsync(settings);
        for (var i = 0; i < 400; i++)
        {
            var dir = Directory.CreateDirectory(Path.Combine(content, i.ToString())).FullName;
            for (var j = 0; j < 15; j++) File.WriteAllText(Path.Combine(dir, j + ".txt"), "");
        }
        using var paused = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        index.ProgressChanged += (_, progress) =>
        {
            if (progress.CurrentPath is not null) { paused.Set(); resume.Wait(TimeSpan.FromSeconds(15)); }
        };
        var rebuild = index.RebuildAsync(settings);
        try
        {
            Assert.True(await Task.Run(() => paused.Wait(TimeSpan.FromSeconds(10))), "The background walk did not publish progress.");
            var hits = await index.SearchAsync("kept").WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(hits);
            Assert.False(rebuild.IsCompleted);
        }
        finally { resume.Set(); await rebuild; }
    }

    [Fact]
    public void Scope_matches_a_directory_boundary_and_reveal_preserves_special_characters()
    {
        CreateIndex(true, ("aether.txt", @"C:\a_100%\aether.txt"), ("aether.txt", @"C:\a_100%other\aether.txt"));
        Assert.Single(NameIndexReader.Search(Database, "aether", @"C:\a_100%\", 40));
        var path = Path.Combine(_root, "中文 & a,b.txt");
        File.WriteAllText(path, "");
        var start = FlowPluginHost.CreateRevealStartInfo(path, @"C:\FilesMate.App.exe");
        Assert.Equal(["/select," + path], start.ArgumentList);
        Assert.Equal("reveal", FlowPluginHost.ContextMenu(path)[0].JsonRPCAction!.Method);
    }

    private void CreateIndex(bool upgraded, params (string Name, string Path)[] rows)
        => CreateIndex(upgraded, rows.Select(row => (row.Name, row.Path, false)).ToArray());

    private void CreateIndex(bool upgraded, params (string Name, string Path, bool IsDirectory)[] rows)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE index_meta(key TEXT PRIMARY KEY,value TEXT); CREATE VIRTUAL TABLE file_name USING fts5(name,path,is_dir UNINDEXED);";
        command.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO file_name VALUES($name,$path,$is_dir);";
        command.Parameters.AddWithValue("$name", "");
        command.Parameters.AddWithValue("$path", "");
        command.Parameters.AddWithValue("$is_dir", "0");
        foreach (var row in rows)
        {
            command.Parameters["$name"].Value = row.Name;
            command.Parameters["$path"].Value = row.Path;
            command.Parameters["$is_dir"].Value = row.IsDirectory ? "1" : "0";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        if (upgraded) NameIndexReader.BuildSubstringIndex(connection, default);
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.SearchRegression")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(_root, true);
    }
}
