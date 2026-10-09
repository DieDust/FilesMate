using System.Globalization;
using FilesMate.App.Models;
using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.Search.Tests;

public sealed class SearchAllocationRegressionTests
{
    [Theory]
    [InlineData("report", false)]
    [InlineData("report | folder:", false)]
    [InlineData("*.pdf !report", false)]
    [InlineData("file:size:>1", false)]
    [InlineData("path:size:>1", false)]
    [InlineData("size:>1", true)]
    [InlineData("SIZE:>1", true)]
    [InlineData("!size:>1", true)]
    [InlineData("report | size:>1", true)]
    [InlineData("\"size:>1\"", true)]
    [InlineData("dm:today", true)]
    [InlineData("!dm:today | report", true)]
    public void Parsed_metadata_requirements_cover_negation_OR_and_quoted_conditions(string text, bool expected) =>
        Assert.Equal(expected, AdvancedSearchQuery.Parse(new(text)).RequiresMetadata);

    [Theory]
    [InlineData("", "", false)]
    [InlineData(" ", "\t", false)]
    [InlineData(">1", "", true)]
    [InlineData("", "today", true)]
    public void Regex_treats_query_as_text_but_preserves_separate_metadata_constraints(string size, string modified, bool expected) =>
        Assert.Equal(expected, AdvancedSearchQuery.Parse(new("size:>1|dm:today", Size: size, Modified: modified, Regex: true)).RequiresMetadata);

    [Theory]
    [InlineData(SearchResultSort.Priority)]
    [InlineData(SearchResultSort.Name)]
    [InlineData(SearchResultSort.NameDescending)]
    [InlineData(SearchResultSort.ModifiedDescending)]
    [InlineData(SearchResultSort.SizeDescending)]
    [InlineData(SearchResultSort.Path)]
    public void Filename_only_predicate_retains_metadata_projection_count_and_page_order(SearchResultSort sort)
    {
        using var fixture = new Fixture();
        var request = new SearchPageRequest("report", Sort: sort);
        var query = AdvancedSearchQuery.Parse(request);
        Assert.False(query.RequiresMetadata);
        var rank = ApplicationCatalog.FileRank(SearchHitKinds.DefaultOrder, request.Query);
        var full = NameIndexReader.SearchAdvanced(fixture.Database, request, query, 20, 0, default, rank: rank);
        Assert.Equal(5, full.TotalCount);
        Assert.Equal(5, full.Hits.Count);
        Assert.False(full.HasMore);
        foreach (var hit in full.Hits)
        {
            var index = int.Parse(hit.Name.AsSpan(7, 1), CultureInfo.InvariantCulture);
            Assert.Equal(index == 0 ? null : index * 10L, hit.Size);
            Assert.Equal(index == 1 ? null : Fixture.Timestamp + index, hit.ModifiedUtcTicks);
        }
        var pages = new List<AdvancedSearchHit>();
        for (var offset = 0; offset < 6; offset += 2)
        {
            var page = NameIndexReader.SearchAdvanced(fixture.Database, request, query, 2, offset, default, rank: rank, includeTotal: false);
            Assert.Null(page.TotalCount);
            Assert.Equal(offset < 4, page.HasMore);
            pages.AddRange(page.Hits);
        }
        Assert.Equal(full.Hits, pages);
        if (sort == SearchResultSort.SizeDescending) Assert.Equal("report-4.pdf", full.Hits[0].Name);
        if (sort == SearchResultSort.ModifiedDescending) Assert.Equal("report-4.pdf", full.Hits[0].Name);
    }

    [Fact]
    public void Callback_arity_follows_parsed_query_instead_of_request_text()
    {
        using var fixture = new Fixture();
        var request = new SearchPageRequest("report");
        var metadataQuery = AdvancedSearchQuery.Parse(new("size:>=30 | dm:2000-01-01"));
        var filtered = NameIndexReader.SearchAdvanced(fixture.Database, request, metadataQuery, 20, 0, default);
        Assert.Equal(["report-3.pdf", "report-4.pdf"], filtered.Hits.Select(hit => hit.Name));
        var filenameQuery = AdvancedSearchQuery.Parse(new("report"));
        var all = NameIndexReader.SearchAdvanced(fixture.Database, new("size:>9999"), filenameQuery, 20, 0, default);
        Assert.Equal(5, all.Hits.Count);
        Assert.Contains(all.Hits, hit => hit.Size == 40);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_callback_shapes_preserve_cancellation_and_reopen(bool metadata)
    {
        using var fixture = new Fixture();
        using var cancel = new CancellationTokenSource();
        var request = new SearchPageRequest(metadata ? "report size:>=0" : "report");
        var query = AdvancedSearchQuery.Parse(request);
        var calls = 0;
        var error = Assert.ThrowsAny<OperationCanceledException>(() => NameIndexReader.SearchAdvanced(fixture.Database, request,
            query, 20, 0, cancel.Token, (_, _) => { if (++calls == 2) cancel.Cancel(); return true; }));
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.Equal(metadata ? 4 : 5, NameIndexReader.SearchAdvanced(fixture.Database, request, query, 20, 0, default).Hits.Count);
        Assert.ThrowsAny<OperationCanceledException>(() => NameIndexReader.SearchAdvanced(fixture.Database, request, query, 20, 0, cancel.Token));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("zh-CN")]
    public void File_rank_preserves_formula_for_both_separator_styles_and_custom_order(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string[] leaves = ["report", "report.pdf", "Report.PDF", "report-final.pdf", "a-report.pdf", ".profile", "report.",
                "报告.pdf", "İıi.md", "Σσς.txt", "straße.pdf", "𐐀𐐨.txt", "a.zip尾", "a.7z.001删", "a.zip.txt", "tool.exe", "tool.COM", "file.lnk", "folder/", "folder\\", ""];
            string[] queries = ["report", " \tREPORT\u2003", "", " ", "报告", "i", "σ", "ss", "𐐀", "a.7z", "folder"];
            foreach (var order in new[] { SearchHitKinds.DefaultOrder, SearchHitKinds.DefaultOrder.Reverse().ToArray() })
            foreach (var query in queries)
            {
                var priorities = SearchHitKinds.SanitizeOrder(order).Select((kind, index) => (kind, index)).ToDictionary(pair => pair.kind, pair => pair.index);
                var rank = ApplicationCatalog.FileRank(order, query);
                foreach (var leaf in leaves)
                foreach (var path in new[] { leaf, "/fixture/目录/" + leaf, "C:\\fixture\\目录\\" + leaf })
                foreach (var directory in new[] { false, true })
                {
                    var expected = priorities[ApplicationCatalog.FileKind(path, directory)] * ApplicationCatalog.RankStride
                        + NameMatchingTests.LegacyRelevance(Path.GetFileName(path), query);
                    Assert.Equal(expected, rank(path, directory));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void File_rank_preserves_deferred_null_failures_and_public_relevance_behavior()
    {
        var nullQueryRank = ApplicationCatalog.FileRank(SearchHitKinds.DefaultOrder, null!);
        Assert.Throws<NullReferenceException>(() => nullQueryRank("report.pdf", false));
        var rank = ApplicationCatalog.FileRank(SearchHitKinds.DefaultOrder, " report ");
        Assert.Throws<NullReferenceException>(() => rank(null!, false));
        Assert.Throws<NullReferenceException>(() => rank(null!, true));
        Assert.Throws<NullReferenceException>(() => NameIndexReader.Relevance(null!, "report"));
        Assert.Throws<NullReferenceException>(() => NameIndexReader.Relevance("report", null!));
    }

    private sealed class Fixture : IDisposable
    {
        internal const long Timestamp = 638975520000000000L;
        private readonly string _root = Directory.CreateTempSubdirectory("FilesMate-allocation-regression-").FullName;
        internal string Database => Path.Combine(_root, "index.db");
        internal Fixture()
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE VIRTUAL TABLE file_name USING fts5(name UNINDEXED,path UNINDEXED,is_dir UNINDEXED); CREATE TABLE file_metadata(rowid INTEGER PRIMARY KEY,size INTEGER,modified INTEGER); CREATE TABLE index_meta(key TEXT PRIMARY KEY,value TEXT);";
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO file_name VALUES($name,$path,'0'); INSERT INTO file_metadata VALUES(last_insert_rowid(),$size,$modified)";
            var name = command.Parameters.Add("$name", SqliteType.Text);
            var path = command.Parameters.Add("$path", SqliteType.Text);
            var size = command.Parameters.Add("$size", SqliteType.Integer);
            var modified = command.Parameters.Add("$modified", SqliteType.Integer);
            for (var i = 0; i < 5; i++)
            {
                name.Value = $"report-{i}.pdf"; path.Value = Path.Combine(_root, (string)name.Value);
                size.Value = i == 0 ? DBNull.Value : i * 10L;
                modified.Value = i == 1 ? DBNull.Value : Timestamp + i;
                command.ExecuteNonQuery();
            }
            NameIndexReader.BuildSubstringIndex(connection, default);
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
}
