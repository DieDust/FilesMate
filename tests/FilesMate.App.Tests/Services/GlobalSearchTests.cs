using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Services;

public sealed class GlobalSearchTests
{
    [Fact]
    public void Legacy_resident_host_conflict_remains_actionable_after_upgrade()
    {
        var reply = System.Text.Json.JsonSerializer.Deserialize<SearchHostReply>(
            """{"Ok":false,"Message":"这个快捷键已被占用，原来的快捷键已保留。","HotkeyRegistered":true,"Pid":123}""")!;
        Assert.True(reply.IsHotkeyConflict);
        Assert.True(reply.HotkeyRegistered);
        Assert.True(new SearchHostReply(false, "", ErrorCode: "HotkeyConflict").IsHotkeyConflict);
        Assert.False(new SearchHostReply(false, "无法保存设置", ErrorCode: "StorageFailure").IsHotkeyConflict);
        Assert.False(new SearchHostReply(true, "快捷键未被占用").IsHotkeyConflict);
    }

    [Fact]
    public void Preview_defaults_on_for_legacy_profiles_and_preserves_opt_out()
    {
        var root = NewDirectory();
        try
        {
            File.WriteAllText(GlobalSearchConfiguration.SettingsPath(root), """{"Enabled":true,"Hotkey":"Alt+Space"}""");
            var legacy = GlobalSearchConfiguration.Load(root);
            Assert.True(legacy.PreviewEnabled);
            GlobalSearchConfiguration.Save(legacy with { PreviewEnabled = false }, root);
            var saved = GlobalSearchConfiguration.Load(root);
            Assert.False(saved.PreviewEnabled);
            GlobalSearchConfiguration.Save(saved with { TrayLeftAction = "Files" }, root);
            Assert.False(GlobalSearchConfiguration.Load(root).PreviewEnabled);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("alt+space", "Alt+Space", 1u, 32u)]
    [InlineData("Shift+CTRL+alt+f10", "Ctrl+Alt+Shift+F10", 7u, 121u)]
    [InlineData("Ctrl+7", "Ctrl+7", 2u, 55u)]
    public void Shortcut_round_trips_to_native_modifier_and_key(string input, string normalized, uint modifiers, uint key)
    {
        Assert.True(SearchHotkey.TryParse(input, out var shortcut));
        Assert.Equal(normalized, shortcut.ToString());
        Assert.Equal(modifiers, shortcut.Modifiers);
        Assert.Equal(key, shortcut.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Space")]
    [InlineData("Alt+Alt+F")]
    [InlineData("Alt+F12")]
    [InlineData("Win+L")]
    [InlineData("Alt+")]
    public void Unsupported_or_reserved_shortcuts_are_rejected(string input) => Assert.False(SearchHotkey.TryParse(input, out _));

    [Fact]
    public void Invalid_shortcut_never_reenables_explicitly_disabled_residency()
    {
        var root = NewDirectory();
        try
        {
            File.WriteAllText(GlobalSearchConfiguration.SettingsPath(root), """{"enabled":false,"hotkey":"invalid"}""");
            var settings = GlobalSearchConfiguration.Load(root);
            Assert.False(settings.Enabled);
            Assert.Equal("Alt+Space", settings.Hotkey);
            GlobalSearchConfiguration.Save(new(false, "shift+ctrl+K"), root);
            Assert.Equal(new(false, "Ctrl+Shift+K"), GlobalSearchConfiguration.Load(root));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Provider_searches_all_indexed_folders_and_marks_limited_results()
    {
        var root = NewDirectory();
        try
        {
            var provider = new IndexedFilesSearchProvider(root);
            Assert.NotNull((await provider.SearchAsync("报告", default)).Notice);
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "search-index.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                for (var i = 0; i < 55; i++)
                {
                    command.CommandText = "INSERT INTO file_name VALUES ($name,$path,'0')";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("$name", $"报告_{i}.txt");
                    command.Parameters.AddWithValue("$path", $@"{(i % 2 == 0 ? "C" : "D")}:\资料\报告_{i}.txt");
                    command.ExecuteNonQuery();
                }
            }
            var result = await provider.SearchAsync("报告", default);
            Assert.True(result.HasMore);
            Assert.Equal(40, result.Hits.Count);
            Assert.Contains(result.Hits, hit => hit.Path.StartsWith("C:", StringComparison.Ordinal));
            Assert.Contains(result.Hits, hit => hit.Path.StartsWith("D:", StringComparison.Ordinal));
            Assert.Empty((await provider.SearchAsync("不存在", default)).Hits);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SearchAsync("报告", canceled.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-global-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task Filters_run_before_limit_and_expansion_keeps_the_ranked_prefix()
    {
        var root = NewDirectory();
        try
        {
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "search-index.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                for (var i = 0; i < 220; i++)
                {
                    command.CommandText = $"INSERT INTO file_name VALUES ('sample_{i}.txt','D:\\sample_{i}.txt','0');";
                    command.ExecuteNonQuery();
                }
                command.CommandText = "INSERT INTO file_name VALUES ('sample.png','D:\\sample.png','0'),('sample.mp4','D:\\sample.mp4','0'),('sample','D:\\sample','1'),('sample.lnk','D:\\sample.lnk','0');";
                command.ExecuteNonQuery();
            }
            var provider = new IndexedFilesSearchProvider(root);
            var first = await provider.SearchAsync("sample", default);
            var more = await provider.SearchAsync("sample", default, limit: 80);
            Assert.Equal(first.Hits, more.Hits.Take(40));
            Assert.Equal(80, more.Hits.Count);
            Assert.Single((await provider.SearchAsync("sample", default, SearchFilter.Images)).Hits);
            Assert.Single((await provider.SearchAsync("sample", default, SearchFilter.Media)).Hits);
            Assert.True(Assert.Single((await provider.SearchAsync("sample", default, SearchFilter.Folders)).Hits).IsDirectory);
            Assert.Empty((await provider.SearchAsync("sample", default, SearchFilter.Apps)).Hits);
            var capped = await provider.SearchAsync("sample", default, SearchFilter.Documents, 1000);
            Assert.Equal(200, capped.Hits.Count);
            Assert.True(capped.HasMore);
            Assert.All(capped.Hits, hit => Assert.EndsWith(".txt", hit.Name));
            var tail = await provider.SearchAsync("sample", default, SearchFilter.Documents, offset: 200);
            Assert.Equal(20, tail.Hits.Count);
            Assert.False(tail.HasMore);
            Assert.Equal(220, capped.Hits.Concat(tail.Hits).Select(hit => hit.Path).Distinct().Count());
            Assert.Empty((await provider.SearchAsync("sample", default, SearchFilter.Documents, offset: 220)).Hits);
            var second = await provider.SearchAsync("sample", default, offset: 40);
            Assert.Equal(more.Hits.Skip(40), second.Hits);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Only_application_targets_are_in_apps_and_ordinary_shortcuts_do_not_outrank_apps()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = NewDirectory();
        try
        {
            var appLink = Path.Combine(root, "sample application.lnk");
            var docLink = Path.Combine(root, "sample document.lnk");
            var folderLink = Path.Combine(root, "sample folder.lnk");
            FilesMate.Platform.Windows.Shell.ShellShortcut.Create(Environment.ProcessPath!, appLink);
            FilesMate.Platform.Windows.Shell.ShellShortcut.Create(Path.Combine(root, "report.txt"), docLink);
            FilesMate.Platform.Windows.Shell.ShellShortcut.Create(root, folderLink);
            using (var db = new SqliteConnection($"Data Source={Path.Combine(root, "search-index.db")};Pooling=False"))
            {
                db.Open();
                using var command = db.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                foreach (var path in new[] { appLink, docLink, folderLink, Path.Combine(root, "sample.exe") })
                {
                    command.CommandText = "INSERT INTO file_name VALUES ($name,$path,'0')";
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("$name", Path.GetFileName(path));
                    command.Parameters.AddWithValue("$path", path);
                    command.ExecuteNonQuery();
                }
            }
            var provider = new IndexedFilesSearchProvider(root);
            var apps = await provider.SearchAsync("sample", default, SearchFilter.Apps);
            Assert.Equal(2, apps.Hits.Count);
            Assert.Equal(appLink, apps.Hits[0].Path);
            Assert.DoesNotContain(apps.Hits, hit => hit.Path == docLink || hit.Path == folderLink);
            Assert.Equal(apps.Hits, (await provider.SearchAsync("sample", default)).Hits.Take(2));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("Files", "Files")]
    [InlineData("files", "Files")]
    [InlineData("Search", "Search")]
    [InlineData("unknown", "Search")]
    public void Tray_action_survives_saving_and_invalid_values_fall_back(string input, string expected)
    {
        var root = NewDirectory();
        try
        {
            GlobalSearchConfiguration.Save(new(false, "Ctrl+K", input), root);
            Assert.Equal(new(false, "Ctrl+K", expected), GlobalSearchConfiguration.Load(root));
            File.WriteAllText(GlobalSearchConfiguration.SettingsPath(root), """{"Enabled":true,"Hotkey":"Alt+Space"}""");
            Assert.Equal("Search", GlobalSearchConfiguration.Load(root).TrayLeftAction);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Ranking_is_applied_before_limit_and_shared_changes_are_visible_next_query()
    {
        var root = NewDirectory();
        try
        {
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "search-index.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                for (var i = 0; i < 100; i++)
                {
                    command.CommandText = $"INSERT INTO file_name VALUES ('sample_{i}.txt','D:\\sample_{i}.txt','0');";
                    command.ExecuteNonQuery();
                }
                command.CommandText = "INSERT INTO file_name VALUES ('sample','D:\\sample','1'),('sample.exe','D:\\sample.exe','0'),('z sample launcher.lnk','D:\\z sample launcher.lnk','0');";
                command.ExecuteNonQuery();
            }
            var provider = new IndexedFilesSearchProvider(root);
            var first = await provider.SearchAsync("sample", default);
            Assert.Equal("sample.exe", first.Hits[0].Name);
            Assert.EndsWith(".txt", first.Hits[1].Name);
            Assert.True(first.HasMore);
            File.WriteAllText(Path.Combine(root, "search-index.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                databaseDirectory = root,
                futureOption = 42,
            }));
            SearchRankingConfiguration.Save([FilesMate.App.Models.SearchHitKind.Folder], root);
            Assert.True((await provider.SearchAsync("sample", default)).Hits[0].IsDirectory);
            var store = new FilesMate.App.Services.SearchIndexSettingsService(Path.Combine(root, "search-index.json"));
            Assert.Equal(FilesMate.App.Models.SearchHitKind.Folder, store.Load().RankOrder[0]);
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.FilePath));
            Assert.Equal(42, saved.RootElement.GetProperty("futureOption").GetInt32());
            Assert.Equal(root, saved.RootElement.GetProperty("roots")[0].GetString());
            await store.SaveAsync(store.Load() with { RankOrder = [FilesMate.App.Models.SearchHitKind.Document] }, updateRankOrder: true);
            Assert.EndsWith(".txt", (await provider.SearchAsync("sample", default)).Hits[0].Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Only_legacy_default_order_is_migrated()
    {
        var legacy = new[] { "Program", "Shortcut", "Folder", "Document", "Image", "Video", "Audio", "Archive", "Code", "Other" }
            .Select(Enum.Parse<FilesMate.App.Models.SearchHitKind>).ToArray();
        Assert.Equal(FilesMate.App.Models.SearchHitKinds.DefaultOrder, FilesMate.App.Models.SearchHitKinds.FromSavedOrder(legacy, 0));
        Assert.Equal(legacy, FilesMate.App.Models.SearchHitKinds.FromSavedOrder(legacy, 2));
        Assert.Equal(FilesMate.App.Models.SearchHitKind.Folder, FilesMate.App.Models.SearchHitKinds.FromSavedOrder([FilesMate.App.Models.SearchHitKind.Folder], 0)[0]);
    }
}
