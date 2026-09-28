using FilesMate.App.Models;
using FilesMate.App.Navigation;
using FilesMate.App.Services;
using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.App.Tests.Search;

public sealed class AdvancedSearchTests
{
    private static AdvancedSearchHit Hit(string name = "年度报告.pdf", string folder = @"D:\资料", long? size = 20 * 1024 * 1024,
        bool directory = false) => new(name, Path.Combine(folder, name), directory, size,
            new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Local).ToUniversalTime().Ticks);
    private static bool Match(string query, AdvancedSearchHit hit) => AdvancedSearchQuery.Parse(new(query), new(2026, 9, 28)).Matches(hit);

    [Theory]
    [InlineData("报告 ext:pdf size:10mb..30mb dm:today", true)]
    [InlineData("报告 !ext:pdf", false)]
    [InlineData("音乐 | 报告", true)]
    [InlineData("年度 报告", true)]
    [InlineData("\"年度 报告\"", false)]
    [InlineData("*.pdf", true)]
    [InlineData("*.docx", false)]
    [InlineData("file: ext:pdf;docx", true)]
    [InlineData("file:*.pdf", true)]
    [InlineData("file:*.docx", false)]
    [InlineData("folder:", false)]
    [InlineData("parent:D:\\资料", true)]
    [InlineData("D:\\资料", true)]
    [InlineData("dm:2026-09-28..2026-09-28", true)]
    [InlineData("dm:2026-09-27", false)]
    [InlineData("size:>20mb", false)]
    [InlineData("size:>=20mb", true)]
    public void Everything_style_conditions_are_combined_before_display(string query, bool expected) => Assert.Equal(expected, Match(query, Hit()));

    [Theory]
    [InlineData("| report")]
    [InlineData("report |")]
    [InlineData("\"report")]
    [InlineData("!")]
    [InlineData("size:20mb..10mb")]
    [InlineData("size:999999999999999999999999999999999tb")]
    [InlineData("ext:")]
    [InlineData("dm:2026-09-28..2026-09-01")]
    [InlineData("content:password")]
    public void Invalid_or_unsupported_conditions_are_not_silently_ignored(string text) => Assert.Throws<ArgumentException>(() => AdvancedSearchQuery.Parse(new(text)));

    [Fact]
    public void Scope_has_directory_boundary_and_case_and_path_modes_are_independent()
    {
        var scoped = AdvancedSearchQuery.Parse(new("报告", @"d:\资料"));
        Assert.True(scoped.Matches(Hit()));
        Assert.False(scoped.Matches(Hit(folder: @"D:\资料备份")));
        Assert.False(AdvancedSearchQuery.Parse(new("资料")).Matches(Hit()));
        Assert.True(AdvancedSearchQuery.Parse(new("资料", MatchPath: true)).Matches(Hit()));
        Assert.False(AdvancedSearchQuery.Parse(new("REPORT", MatchCase: true)).Matches(Hit("report.pdf")));
        Assert.True(AdvancedSearchQuery.Parse(new(@"^年度.*\.pdf$", Regex: true)).Matches(Hit()));
        Assert.Throws<ArgumentException>(() => AdvancedSearchQuery.Parse(new(@"(a)\1", Regex: true)));
        Assert.False(Match("size:>0", Hit(size: null)));
    }

    [Fact]
    public void Request_roundtrip_preserves_filters_and_shell_launch_does_not_treat_query_as_command()
    {
        var request = new SearchPageRequest("\"a b\" | !*.exe", @"D:\资料", "Documents", "pdf", ">1mb", "7days", true, true, false, SearchResultSort.ModifiedDescending);
        Assert.True(SearchPageRequest.TryParse(request.Location, out var parsed)); Assert.Equal(request, parsed);
        Assert.Equal(request.Location, LaunchPath.Parse(["--search-page", request.Location]).SearchPage);
        Assert.False(SearchPageRequest.TryParse(SearchPageRequest.Prefix + "!!!", out _));
        Assert.False(SearchPageRequest.TryParse(new SearchPageRequest(new string('a', 2049)).Location, out _));
        Assert.False(new LaunchTarget(null, null) { SearchPage = request.Location }.SameDestination(new(null, null)));
    }

    [Fact]
    public async Task Rebuild_records_real_metadata_and_advanced_filters_keep_legacy_search_working()
    {
        var root = Directory.CreateTempSubdirectory("FilesMate-advanced-metadata-").FullName;
        try
        {
            var name = Path.Combine(root, "report.pdf");
            await File.WriteAllBytesAsync(name, new byte[12345]);
            await using var index = new FileNameIndexService(Path.Combine(root, "index", "search-index.db"));
            await index.RebuildAsync(SearchIndexSettings.Sanitize([root], [], false, 1));
            var request = new SearchPageRequest("report ext:pdf size:>10kb dm:today");
            var response = NameIndexReader.SearchAdvanced(index.FilePath, request, AdvancedSearchQuery.Parse(request), 200, 0, default);
            Assert.Equal(12345, Assert.Single(response.Hits).Size);
            Assert.Contains(await index.SearchAsync("report"), h => h.Name == "report.pdf");
            Assert.False(index.NeedsUpgrade);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(SearchResultSort.Priority)]
    [InlineData(SearchResultSort.Name)]
    [InlineData(SearchResultSort.NameDescending)]
    [InlineData(SearchResultSort.SizeDescending)]
    [InlineData(SearchResultSort.ModifiedDescending)]
    [InlineData(SearchResultSort.Path)]
    public async Task File_and_application_pages_have_no_missing_or_repeated_results(SearchResultSort sort)
    {
        using var fixture = new IndexFixture();
        var apps = Enumerable.Range(0, 17).Select(i => new ApplicationEntry("app" + i, "report" + (i * 3), "shell:AppsFolder\\" + i)).ToArray();
        var provider = new AdvancedSearchProvider(new Catalog(apps), fixture.Root);
        var request = new SearchPageRequest("report", Sort: sort);
        var all = new List<AdvancedSearchHit>();
        for (var offset = 0; ; offset += 17)
        {
            var page = await provider.SearchAsync(fixture.Database, request, offset, default, 17);
            all.AddRange(page.Hits); if (!page.HasMore) break;
            Assert.True(offset < 400);
        }
        Assert.Equal(137 + 17, all.Count);
        Assert.Equal(all.Count, all.Select(h => h.Path).Distinct().Count());
        var full = await provider.SearchAsync(fixture.Database, request, 0, default, 200);
        Assert.Equal(full.Hits, all);
        if (sort == SearchResultSort.SizeDescending) Assert.Equal(136, all[0].Size);
    }

    [Fact]
    public async Task Filtering_sorting_and_custom_categories_precede_pagination_and_settings_are_shared()
    {
        using var fixture = new IndexFixture();
        SearchCategories.Save(fixture.Root, [.. SearchCategories.Defaults(), new("pdf-only", "PDF", null, [".pdf"])]);
        var provider = new AdvancedSearchProvider(new Catalog([]), fixture.Root);
        var request = new SearchPageRequest("report size:>120", CategoryId: "pdf-only", Sort: SearchResultSort.SizeDescending);
        var page = await provider.SearchAsync(fixture.Database, request, 0, default, 5);
        Assert.Equal([136L, 135, 134, 133, 132], page.Hits.Select(h => h.Size!.Value));
        Assert.True(page.HasMore);
        var next = await provider.SearchAsync(fixture.Database, request, 5, default, 5);
        Assert.Equal(131, next.Hits[0].Size);
        HiddenSearchResults.Hide([new("report136.pdf", @"D:\docs\report136.pdf", false)], fixture.Root);
        var hidden = await provider.SearchAsync(fixture.Database, request, 0, default, 5);
        Assert.Equal(135, hidden.Hits[0].Size);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SearchAsync(fixture.Database, request, 0, canceled.Token));
    }

    [Fact]
    public void Legacy_index_explains_missing_metadata_without_making_up_zeroes()
    {
        using var fixture = new IndexFixture();
        using (var connection = new SqliteConnection($"Data Source={fixture.Database};Pooling=False"))
        { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE file_metadata;"; command.ExecuteNonQuery(); }
        var query = new SearchPageRequest("report size:>1");
        var response = NameIndexReader.SearchAdvanced(fixture.Database, query, AdvancedSearchQuery.Parse(query), 20, 0, default);
        Assert.Empty(response.Hits); Assert.Equal("SearchPage_MetadataRequired", response.Notice);
        query = new("report");
        Assert.Equal(20, NameIndexReader.SearchAdvanced(fixture.Database, query, AdvancedSearchQuery.Parse(query), 20, 0, default).Hits.Count);
    }

    [Fact]
    public void Cancelling_a_running_index_scan_interrupts_sqlite()
    {
        using var fixture = new IndexFixture();
        using var cancellation = new CancellationTokenSource();
        var query = new SearchPageRequest("report");
        var checkedRows = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => NameIndexReader.SearchAdvanced(fixture.Database, query,
            AdvancedSearchQuery.Parse(query), 200, 0, cancellation.Token, (_, _) =>
            {
                if (++checkedRows == 20) cancellation.Cancel();
                return true;
            }));
        Assert.InRange(checkedRows, 20, 21);
    }

    private sealed class Catalog(IReadOnlyList<ApplicationEntry> entries) : IApplicationCatalog
    { public Task<IReadOnlyList<ApplicationEntry>> GetAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(entries); } }

    [Theory]
    [InlineData(SearchResultSort.Priority)]
    [InlineData(SearchResultSort.Name)]
    [InlineData(SearchResultSort.NameDescending)]
    [InlineData(SearchResultSort.SizeDescending)]
    [InlineData(SearchResultSort.ModifiedDescending)]
    [InlineData(SearchResultSort.Path)]
    public async Task Palette_and_page_share_order_filters_and_application_aliases(SearchResultSort sort)
    {
        using var fixture = new IndexFixture();
        var catalog = new Catalog([new("app-report", "Report editor", "shell:AppsFolder\\Editor"),
            new("alias", "文档编辑", "shell:AppsFolder\\Alias", @"C:\apps\report.exe")]);
        SearchSortConfiguration.Save(sort, fixture.Root);
        var page = await new AdvancedSearchProvider(catalog, fixture.Root).SearchAsync(fixture.Database, new("report", Sort: sort), 0, default);
        var palette = new ConfiguredSearchProvider(catalog, fixture.Root);
        var hits = new List<NameHit>();
        for (var offset = 0; ; offset += 40)
        {
            var response = await palette.SearchAsync("report", default, limit: 40, offset: offset);
            hits.AddRange(response.Hits); if (!response.HasMore) break;
            Assert.True(offset < 200);
        }
        Assert.Equal(page.Hits.Select(h => h.Path), hits.Select(h => h.Path));
        Assert.Contains(hits, h => h.Application?.Id == "alias");
        Assert.Equal(139, page.TotalCount);
        SearchRankingConfiguration.Save(SearchHitKinds.DefaultOrder.Reverse(), fixture.Root);
        Assert.Equal(SearchResultSort.Priority, SearchSortConfiguration.Load(fixture.Root));
    }

    [Fact]
    public async Task Total_count_respects_filters_without_loading_all_rows()
    {
        using var fixture = new IndexFixture();
        var provider = new AdvancedSearchProvider(new Catalog([]), fixture.Root);
        var query = new SearchPageRequest("report", Size: ">120");
        var first = await provider.SearchAsync(fixture.Database, query, 0, default, 5);
        Assert.Equal(16, first.TotalCount); Assert.Equal(5, first.Hits.Count);
        var second = await provider.SearchAsync(fixture.Database, query, 5, default, 5);
        Assert.Null(second.TotalCount); Assert.Equal(5, second.Hits.Count);
        Assert.DoesNotContain(second.Hits, first.Hits.Contains);
    }

    private sealed class IndexFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("FilesMate-advanced-index-").FullName;
        public string Database => Path.Combine(Root, "search-index.db");
        public IndexFixture()
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False"); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT); CREATE TABLE file_metadata(rowid INTEGER PRIMARY KEY,size INTEGER,modified INTEGER); CREATE TABLE index_meta(key TEXT PRIMARY KEY,value TEXT);";
            command.ExecuteNonQuery();
            using var tx = connection.BeginTransaction(); command.Transaction = tx;
            for (var i = 0; i < 137; i++)
            {
                command.CommandText = "INSERT INTO file_name VALUES ($name,$path,'0'); INSERT INTO file_metadata VALUES (last_insert_rowid(),$size,$date);";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$name", $"report{i}.pdf"); command.Parameters.AddWithValue("$path", $@"D:\docs\report{i}.pdf");
                command.Parameters.AddWithValue("$size", i); command.Parameters.AddWithValue("$date", DateTime.UtcNow.Date.AddMinutes(i).Ticks); command.ExecuteNonQuery();
            }
            tx.Commit(); NameIndexReader.BuildSubstringIndex(connection, default);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
