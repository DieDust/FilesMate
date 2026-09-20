using FilesMate.App.Models;
using FilesMate.App.Services;

namespace FilesMate.App.Tests.Search;

public sealed class FileNameIndexTests
{
    [Fact]
    public void Path_rules_match_prefixes_and_folder_names()
    {
        string[] exclusions = [@"C:\Windows", "node_modules", "WinSxS"];
        Assert.True(SearchIndexPathRules.IsExcluded(@"C:\Windows\System32", exclusions));
        Assert.False(SearchIndexPathRules.IsExcluded(@"C:\Windows.old\file", exclusions));
        Assert.True(SearchIndexPathRules.IsExcluded(@"D:\code\app\node_modules\pkg", exclusions));
        Assert.True(SearchIndexPathRules.IsExcluded(@"C:\Windows\WinSxS\manifests", exclusions));
        Assert.False(SearchIndexPathRules.IsExcluded(@"D:\Documents\report.txt", exclusions));
    }

    [Fact]
    public void Directory_scope_normalizes_home_and_folder_boundaries()
    {
        Assert.True(FileNameIndexService.TryGetPathPrefix(null, out var global));
        Assert.Null(global);
        Assert.True(FileNameIndexService.TryGetPathPrefix("filesmate:home", out var home));
        Assert.Null(home);
        var folder = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.True(FileNameIndexService.TryGetPathPrefix(folder, out var prefix));
            Assert.Equal(Path.GetFullPath(folder) + "\\", prefix);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public async Task Parallel_rebuild_over_several_roots_indexes_every_entry_exactly_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var db = Path.Combine(root, "db", "index.db");
        var roots = new List<string>();
        var expectedFiles = 0;
        for (var r = 0; r < 4; r++)
        {
            var top = Directory.CreateDirectory(Path.Combine(root, "root" + r)).FullName;
            roots.Add(top);
            for (var d = 0; d < 30; d++)
            {
                var dir = Directory.CreateDirectory(Path.Combine(top, "dir" + d)).FullName;
                for (var f = 0; f < 20; f++) { File.WriteAllText(Path.Combine(dir, $"Parallel-{r}-{d}-{f}.txt"), ""); expectedFiles++; }
            }
        }
        await using var index = new FileNameIndexService(db);
        try
        {
            await index.RebuildAsync(SearchIndexSettings.Sanitize(roots, [], scanInParallel: true, maxDepth: 0));
            Assert.Equal(expectedFiles, index.Stats.Files);
            Assert.Equal(4 * 30, index.Stats.Folders);
            Assert.Equal(0, index.Stats.Errors);
            var hits = FilesMate.Search.NameIndexReader.Search(db, "Parallel-3-29", null, 100);
            Assert.Equal(20, hits.Count);
            Assert.Equal(20, hits.Select(hit => hit.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // A one-character query has no trigram to use; it must still answer (bounded scan) and page consistently.
            var first = FilesMate.Search.NameIndexReader.Search(db, "P", null, 40);
            var second = FilesMate.Search.NameIndexReader.Search(db, "P", null, 40, offset: 40);
            Assert.Equal(40, first.Count);
            Assert.Equal(40, second.Count);
            Assert.Empty(first.Select(hit => hit.Path).Intersect(second.Select(hit => hit.Path), StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rebuild_indexes_names_and_skips_excluded_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var db = Path.Combine(root, "index.db");
        Directory.CreateDirectory(Path.Combine(root, "keep"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules"));
        File.WriteAllText(Path.Combine(root, "keep", "QuarterReport.txt"), "ok");
        File.WriteAllText(Path.Combine(root, "node_modules", "secret.txt"), "no");
        await using var index = new FileNameIndexService(db);
        try
        {
            await index.RebuildAsync(SearchIndexSettings.Sanitize([root], ["node_modules"], false));
            var hits = await index.SearchAsync("Quarter");
            Assert.Contains(hits, hit => hit.Name.Equals("QuarterReport.txt", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(hits, hit => hit.Name.Equals("secret.txt", StringComparison.OrdinalIgnoreCase));
            Assert.True(index.Stats.Files >= 1);
            Assert.Empty(Directory.EnumerateFiles(root, "index.db.rebuild-*.db*"));

            Directory.CreateDirectory(Path.Combine(root, "other"));
            File.WriteAllText(Path.Combine(root, "other", "QuarterNotes.txt"), "other");
            await index.RebuildAsync(SearchIndexSettings.Sanitize([root], ["node_modules"], false));
            var scoped = await index.SearchAsync("Quarter", Path.Combine(root, "keep"));
            Assert.Contains(scoped, hit => hit.Name.Equals("QuarterReport.txt", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(scoped, hit => hit.Name.Equals("QuarterNotes.txt", StringComparison.OrdinalIgnoreCase));
            var elsewhere = await index.SearchAsync("Quarter", Path.Combine(root, "other"));
            Assert.Contains(elsewhere, hit => hit.Name.Equals("QuarterNotes.txt", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(elsewhere, hit => hit.Name.Equals("QuarterReport.txt", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(Directory.EnumerateFiles(root, "index.db.rebuild-*.db*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rebuild_stops_at_max_folder_depth()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var db = Path.Combine(root, "index.db");
        Directory.CreateDirectory(Path.Combine(root, "keep", "deep"));
        File.WriteAllText(Path.Combine(root, "keep", "QuarterReport.txt"), "ok");
        File.WriteAllText(Path.Combine(root, "keep", "deep", "Nested.txt"), "deep");
        await using var index = new FileNameIndexService(db);
        try
        {
            await index.RebuildAsync(SearchIndexSettings.Sanitize([root], [], false, maxDepth: 1));
            var shallow = await index.SearchAsync("Quarter");
            Assert.DoesNotContain(shallow, hit => hit.Name.Equals("QuarterReport.txt", StringComparison.OrdinalIgnoreCase));

            await index.RebuildAsync(SearchIndexSettings.Sanitize([root], [], false, maxDepth: 2));
            var mid = await index.SearchAsync("Quarter");
            Assert.Contains(mid, hit => hit.Name.Equals("QuarterReport.txt", StringComparison.OrdinalIgnoreCase));
            var nested = await index.SearchAsync("Nested");
            Assert.DoesNotContain(nested, hit => hit.Name.Equals("Nested.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_cancels_and_waits_for_an_active_rebuild_without_disposing_its_gate_early()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "content");
        var db = Path.Combine(root, "index.db");
        Directory.CreateDirectory(content);
        for (var folderIndex = 0; folderIndex < 120; folderIndex++)
        {
            var folder = Path.Combine(content, $"folder-{folderIndex:D3}");
            Directory.CreateDirectory(folder);
            for (var fileIndex = 0; fileIndex < 20; fileIndex++)
            {
                File.WriteAllText(Path.Combine(folder, $"item-{fileIndex:D2}.txt"), "x");
            }
        }

        var index = new FileNameIndexService(db);
        try
        {
            var rebuild = index.RebuildAsync(SearchIndexSettings.Sanitize([content], [], false));
            await Task.Delay(10);
            await index.DisposeAsync();
            var failure = await Record.ExceptionAsync(() => rebuild);

            Assert.True(
                failure is null or OperationCanceledException,
                $"Dispose raced the active rebuild with {failure?.GetType().Name}: {failure}");
        }
        finally
        {
            await index.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Superseded_rebuilds_release_the_gate_and_clear_running_state_when_disposed()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "content");
        var db = Path.Combine(root, "index.db");
        Directory.CreateDirectory(content);
        for (var folderIndex = 0; folderIndex < 80; folderIndex++)
        {
            var folder = Path.Combine(content, $"folder-{folderIndex:D3}");
            Directory.CreateDirectory(folder);
            for (var fileIndex = 0; fileIndex < 20; fileIndex++)
            {
                File.WriteAllText(Path.Combine(folder, $"item-{fileIndex:D2}.txt"), "x");
            }
        }

        var index = new FileNameIndexService(db);
        try
        {
            var settings = SearchIndexSettings.Sanitize([content], [], false);
            var first = index.RebuildAsync(settings);
            await Task.Delay(10);
            var second = index.RebuildAsync(settings);
            await Task.Delay(10);
            await index.DisposeAsync();
            var failures = await Task.WhenAll(
                Record.ExceptionAsync(() => first),
                Record.ExceptionAsync(() => second));

            Assert.All(failures, failure =>
                Assert.True(failure is null or OperationCanceledException, failure?.ToString()));
            Assert.False(index.IsRunning);
        }
        finally
        {
            await index.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Settings_persist_depth_and_database_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var json = Path.Combine(root, "search-index.json");
        var store = new SearchIndexSettingsService(json);
        try
        {
            var settings = SearchIndexSettings.Sanitize([root], ["node_modules"], true, 3, root);
            Assert.Equal(3, settings.MaxDepth);
            Assert.Equal(Path.GetFullPath(root), settings.DatabaseDirectory);
            Assert.Equal(Path.Combine(Path.GetFullPath(root), SearchIndexSettings.DatabaseFileName), settings.ResolveDatabasePath());
            await store.SaveAsync(settings);
            var loaded = store.Load();
            Assert.Equal(3, loaded.MaxDepth);
            Assert.Equal(Path.GetFullPath(root), loaded.DatabaseDirectory);
            Assert.True(loaded.ScanInParallel);
            Assert.True(loaded.AutoRefresh);
            Assert.Equal(SearchHitKinds.DefaultOrder, loaded.RankOrder);

            var custom = SearchHitKinds.SanitizeOrder(
            [
                SearchHitKind.Folder,
                SearchHitKind.Program,
            ]);
            var paused = SearchIndexSettings.Sanitize([root], ["node_modules"], true, 3, root, false, custom);
            await store.SaveAsync(paused);
            var reloaded = store.Load();
            Assert.False(reloaded.AutoRefresh);
            Assert.Equal(SearchHitKind.Folder, reloaded.RankOrder[0]);
            Assert.Equal(SearchHitKind.Program, reloaded.RankOrder[1]);
            Assert.Equal(SearchHitKinds.DefaultOrder.Count, reloaded.RankOrder.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void First_run_and_missing_roots_select_all_ready_local_disks_with_six_level_depth()
    {
        var expected = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName).ToArray();
        Assert.NotEmpty(expected);
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "search-index.json");
        var store = new SearchIndexSettingsService(path);
        try
        {
            Assert.Equal(expected, store.Load().Roots);
            Assert.Equal(6, store.Load().MaxDepth);
            Directory.CreateDirectory(directory);
            // Global search may save ranking before the file manager initializes indexing.
            File.WriteAllText(path, "{\"rankOrder\":[\"Program\"]}");
            Assert.Equal(expected, store.Load().Roots);
            Assert.Equal(6, store.Load().MaxDepth);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Existing_disk_scope_and_depth_are_preserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = Path.GetPathRoot(directory)!;
            var path = Path.Combine(directory, "search-index.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { roots = new[] { root }, maxDepth = 6 }));
            var settings = new SearchIndexSettingsService(path).Load();
            Assert.Equal(new[] { root }, settings.Roots);
            Assert.Equal(6, settings.MaxDepth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Sanitize_clamps_depth_and_rejects_file_paths_as_directories()
    {
        Assert.Equal(SearchIndexSettings.DefaultMaxDepth, SearchIndexSettings.Default.MaxDepth);
        Assert.True(SearchIndexSettings.Default.AutoRefresh);
        Assert.Equal(SearchHitKinds.DefaultOrder, SearchIndexSettings.Default.RankOrder);
        Assert.Equal(SearchIndexSettings.DefaultMaxDepth, SearchIndexSettings.Sanitize(null, null, false).MaxDepth);
        Assert.Equal(0, SearchIndexSettings.Sanitize(null, null, false, 0).MaxDepth);
        Assert.Equal(0, SearchIndexSettings.Sanitize(null, null, false, -4).MaxDepth);
        Assert.Equal(SearchIndexSettings.MaxAllowedDepth, SearchIndexSettings.Sanitize(null, null, false, 99).MaxDepth);

        var file = Path.Combine(Path.GetTempPath(), "FilesMate", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
        try
        {
            var settings = SearchIndexSettings.Sanitize(null, null, false, 0, file);
            Assert.Equal(SearchIndexSettings.DefaultDatabaseDirectory, settings.DatabaseDirectory);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Freshness_builds_an_empty_index_and_refreshes_stale_completed_indexes()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        Assert.True(SearchIndexFreshness.ShouldRefresh(SearchIndexStats.Empty, now, true, false));
        Assert.False(SearchIndexFreshness.ShouldRefresh(new(1, 0, 0, now.AddHours(-25)), now, false, false));
        Assert.False(SearchIndexFreshness.ShouldRefresh(new(1, 0, 0, now.AddHours(-25)), now, true, true));
        Assert.False(SearchIndexFreshness.ShouldRefresh(new(1, 0, 0, now.AddHours(-1)), now, true, false));
        Assert.True(SearchIndexFreshness.ShouldRefresh(new(1, 0, 0, now.AddHours(-25)), now, true, false));
    }

    [Fact]
    public void App_refreshes_a_stale_index_in_the_background()
    {
        var app = File.ReadAllText(Path.Combine(
            FilesMate.App.Tests.DesignSystem.ThemeXaml.AppRoot,
            "App.xaml.cs"));
        Assert.Contains("RunAutoIndexAsync", app, StringComparison.Ordinal);
        Assert.Contains("SearchIndexFreshness.ShouldRefresh", app, StringComparison.Ordinal);
        Assert.Contains("SearchIndexFreshness.StartupDelay", app, StringComparison.Ordinal);
        Assert.Contains("SearchIndexFreshness.RecheckEvery", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Ranking_puts_programs_first_then_sorts_names()
    {
        HomeSearchHit[] hits =
        [
            new("notes.txt", @"C:\notes.txt", false),
            new("zeta.exe", @"C:\zeta.exe", false),
            new("alpha.exe", @"C:\alpha.exe", false),
            new("Pictures", @"C:\Pictures", true),
            new("shot.png", @"C:\shot.png", false),
        ];

        var sorted = SearchHitRanking.Sort(hits, SearchHitKinds.DefaultOrder, 80);
        Assert.Equal(["alpha.exe", "zeta.exe", "notes.txt", "shot.png", "Pictures"], sorted.Select(hit => hit.Name));

        var foldersFirst = SearchHitRanking.Sort(hits, [SearchHitKind.Folder, SearchHitKind.Program], 80);
        Assert.Equal("Pictures", foldersFirst[0].Name);
        Assert.Equal("alpha.exe", foldersFirst[1].Name);
        Assert.Equal("zeta.exe", foldersFirst[2].Name);
    }

    [Fact]
    public void Ranking_sanitize_appends_missing_kinds_and_drops_duplicates()
    {
        var order = SearchHitKinds.SanitizeOrder(
        [
            SearchHitKind.Folder,
            SearchHitKind.Folder,
            SearchHitKind.Program,
        ]);
        Assert.Equal(SearchHitKind.Folder, order[0]);
        Assert.Equal(SearchHitKind.Program, order[1]);
        Assert.Equal(SearchHitKinds.DefaultOrder.Count, order.Count);
        Assert.Equal(order.Count, order.Distinct().Count());
    }

    [Fact]
    public void Ranking_classifies_executables_and_common_types()
    {
        Assert.Equal(SearchHitKind.Program, SearchHitRanking.Classify(@"C:\App.exe", false));
        Assert.Equal(SearchHitKind.Program, SearchHitRanking.Classify(@"C:\tool.com", false));
        Assert.Equal(SearchHitKind.Shortcut, SearchHitRanking.Classify(@"C:\App.lnk", false));
        Assert.Equal(SearchHitKind.Folder, SearchHitRanking.Classify(@"C:\Docs", true));
        Assert.Equal(SearchHitKind.Document, SearchHitRanking.Classify(@"C:\a.txt", false));
        Assert.Equal(SearchHitKind.Image, SearchHitRanking.Classify(@"C:\a.png", false));
        Assert.Equal(SearchHitKind.Code, SearchHitRanking.Classify(@"C:\a.cs", false));
    }
}
