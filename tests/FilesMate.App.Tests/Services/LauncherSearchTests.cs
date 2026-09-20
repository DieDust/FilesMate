using FilesMate.App.Models;
using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Services;

public sealed class LauncherSearchTests
{
    [Fact]
    public async Task Apps_work_without_a_file_index_and_use_friendly_names()
    {
        var provider = new LauncherSearchProvider(new Catalog([
            new("office", "Excel", "shell:AppsFolder\\office", @"C:\Office\EXCEL.EXE"),
            new("store", "记事本", "shell:AppsFolder\\Microsoft.WindowsNotepad!App")]), Path.GetTempPath());
        var apps = await provider.SearchAsync("excel", default, SearchFilter.Apps);
        Assert.Equal("Excel", Assert.Single(apps.Hits).Name);
        Assert.NotNull(apps.Hits[0].Application);
        Assert.Single((await provider.SearchAsync("记事本", default, SearchFilter.Apps)).Hits);
    }

    [Fact]
    public void Catalog_deduplicates_aliases_but_preserves_distinct_launch_arguments()
    {
        var entries = ApplicationCatalog.Normalize([
            new("chrome", "Google Chrome", "shell:AppsFolder\\Chrome", @"C:\chrome.exe"),
            new("desktop", "Chrome", @"C:\Desktop\Chrome.lnk", @"C:\chrome.exe"),
            new("webapp", "Mail", "shell:AppsFolder\\Mail", @"C:\chrome.exe", "--app=mail"),
            new("remove", "卸载 Chrome", @"C:\uninstall.exe", @"C:\uninstall.exe")]);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Name == "Mail");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Merged_pages_are_complete_and_stable_with_custom_type_order(bool documentsFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "search-index.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                for (var i = 0; i < 137; i++)
                {
                    command.CommandText = $"INSERT INTO file_name VALUES ('match{i}.txt','C:\\docs\\match{i}.txt','0');";
                    command.ExecuteNonQuery();
                }
                command.CommandText = "INSERT INTO file_name VALUES ('match.exe','C:\\build\\match.exe','0'),('match-helper.exe','C:\\build\\match-helper.exe','0');";
                command.ExecuteNonQuery();
            }
            if (documentsFirst) SearchRankingConfiguration.Save([SearchHitKind.Document, SearchHitKind.Program], root);
            var provider = new LauncherSearchProvider(new Catalog(Enumerable.Range(0, 13)
                .Select(i => new ApplicationEntry("app" + i, "match app " + i, "shell:AppsFolder\\app" + i)).ToArray()), root);
            var first = await provider.SearchAsync("match", default);
            Assert.Equal(!documentsFirst, first.Hits[0].Application is not null);
            var all = new List<NameHit>();
            for (var page = 0; page < 10; page++)
            {
                var response = await provider.SearchAsync("match", default, limit: 17, offset: all.Count);
                all.AddRange(response.Hits);
                if (!response.HasMore) break;
            }
            Assert.Equal(152, all.Count);
            Assert.Equal(152, all.Select(hit => hit.Path).Distinct().Count());
            Assert.Equal(13, all.Count(hit => hit.Application is not null));
            Assert.Contains(all, hit => hit.Name == "match-helper.exe" && hit.Application is null);
            Assert.Equal(all.Take(40), first.Hits);
            var apps = await provider.SearchAsync("match", default, SearchFilter.Apps);
            Assert.Equal(13, apps.Hits.Count);
            Assert.All(apps.Hits, hit => Assert.NotNull(hit.Application));
            Assert.Empty((await provider.SearchAsync("helper", default, SearchFilter.Apps)).Hits);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Ordinary_executables_have_file_priority() => Assert.Equal(SearchHitKind.Other, ApplicationCatalog.FileKind(@"C:\build\FilesMate.App.exe", false));

    [Fact]
    public async Task Software_name_matches_precede_apps_using_the_same_host_executable()
    {
        var provider = new LauncherSearchProvider(new Catalog([
            new("browser", "Google Chrome", "shell:AppsFolder\\Chrome", @"C:\chrome.exe"),
            new("mail", "Mail", "shell:AppsFolder\\Mail", @"C:\chrome.exe", "--app=mail")]), Path.GetTempPath());
        var result = await provider.SearchAsync("chrome", default, SearchFilter.Apps);
        Assert.Equal("Google Chrome", result.Hits[0].Name);
    }

    [Fact]
    public async Task Hidden_web_app_does_not_hide_browser_and_can_be_restored_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-hide-" + Guid.NewGuid().ToString("N"));
        var browser = new ApplicationEntry("chrome", "Chrome", "shell:AppsFolder\\Chrome", @"C:\chrome.exe");
        var web = new ApplicationEntry("mail", "Chrome Mail", "shell:AppsFolder\\Mail", @"C:\chrome.exe", "--app=mail");
        var link = web with { Id = "link", LaunchPath = @"C:\Desktop\Mail.lnk" };
        try
        {
            Assert.Null(web.LocationPath);
            Assert.Equal(link.LaunchPath, link.LocationPath);
            Assert.Equal(link, Assert.Single(ApplicationCatalog.Normalize([web, link])));
            HiddenSearchResults.Hide([web.ToHit()], root);
            var provider = new LauncherSearchProvider(new Catalog([browser, web, link]), root);
            Assert.Equal(browser, Assert.Single((await provider.SearchAsync("chrome", default, SearchFilter.Apps)).Hits).Application);
            HiddenSearchResults.Restore(HiddenSearchResults.Load(root).Keys, root);
            Assert.Equal(2, (await provider.SearchAsync("chrome", default, SearchFilter.Apps)).Hits.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Hidden_files_are_filtered_before_pagination_and_restore_without_reindexing()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-hide-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "search-index.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);";
                command.ExecuteNonQuery();
                for (var i = 0; i < 85; i++)
                {
                    command.CommandText = $"INSERT INTO file_name VALUES ('match{i}.txt','C:\\docs\\match{i}.txt','0');";
                    command.ExecuteNonQuery();
                }
            }
            var hidden = Enumerable.Range(0, 50).Select(i => new NameHit($"match{i}.txt", $@"C:\docs\match{i}.txt", false)).ToArray();
            HiddenSearchResults.Hide(hidden, root);
            var provider = new IndexedFilesSearchProvider(root);
            var all = new List<NameHit>();
            for (var page = 0; page < 10; page++)
            {
                var result = await provider.SearchAsync("match", default, SearchFilter.Documents, 7, all.Count);
                all.AddRange(result.Hits);
                if (!result.HasMore) break;
            }
            Assert.Equal(35, all.Count);
            Assert.Equal(35, all.Select(hit => hit.Path).Distinct().Count());
            Assert.DoesNotContain(all, hit => hidden.Any(item => item.Path == hit.Path));
            HiddenSearchResults.Restore([HiddenSearchResults.Key(hidden[0])], root);
            Assert.Equal(36, (await provider.SearchAsync("match", default, limit: 100)).Hits.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Catalog(IReadOnlyList<ApplicationEntry> entries) : IApplicationCatalog
    {
        public Task<IReadOnlyList<ApplicationEntry>> GetAsync(CancellationToken token) => Task.FromResult(entries);
    }
}
