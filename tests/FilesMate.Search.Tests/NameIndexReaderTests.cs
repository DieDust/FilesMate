using FilesMate.Search;
using Microsoft.Data.Sqlite;

namespace FilesMate.Search.Tests;

public sealed class NameIndexReaderTests
{
    public enum BaseTable { Plain, LegacyFts, ProductionFts }

    public static IEnumerable<object[]> SchemasAndTables()
    {
        foreach (var schema in new[] { 1, 2, 3 })
        foreach (var table in Enum.GetValues<BaseTable>()) yield return [schema, table];
    }

    public static IEnumerable<object[]> SchemasTablesAndFlags()
    {
        foreach (var row in SchemasAndTables())
        foreach (var flag in new[] { false, true }) yield return [row[0], row[1], flag];
    }

    [Theory]
    [MemberData(nameof(SchemasAndTables))]
    public void Every_schema_preserves_short_CJK_literal_and_multi_term_queries(int schema, BaseTable table)
    {
        using var fixture = new IndexFixture(schema,
            ["a", "ab.txt", "MyAetherSwap.exe", "aether.exe", "中文测试文档.txt", "report_100%.txt",
                "reportA100X.txt", "été.txt", "a[b]*?\".txt", "ordinary.txt"], table: table);
        string[] queries = ["a", "ab", "aether", "swap exe", "aether missing", "文", "文档", "中文 文档", "ÉTÉ",
            "_100%", "%", "*?", "[b]", "*?\"", "ordinary", " ", "missing"];
        foreach (var query in queries)
        {
            var expected = fixture.Names.Where(name => NameMatchingTests.LegacyMatches(name, NameIndexReader.Terms(query))).Order();
            var actual = NameIndexReader.Search(fixture.Database, query, null, 100).Select(hit => hit.Name).Order();
            Assert.Equal(expected, actual);
        }
        Assert.Empty(NameIndexReader.Search(fixture.Database, "parent-only", null, 100));
    }

    [Theory]
    [InlineData(BaseTable.Plain)]
    [InlineData(BaseTable.LegacyFts)]
    [InlineData(BaseTable.ProductionFts)]
    public void Encoded_schema_preserves_supplementary_unicode_and_does_not_scan_absent_short_queries(BaseTable table)
    {
        using var fixture = new IndexFixture(3, ["𐐨-report.txt", "报告🎉甲.txt", "资料🇯🇵.txt", "𝒜pp𝒷.txt"], table: table);
        foreach (var query in new[] { "𐐀", "𐐨", "报", "报告", "🎉", "报告🎉", "🇯🇵", "𝒜pp" })
        {
            var expected = fixture.Names.Where(name => NameMatchingTests.LegacyMatches(name, NameIndexReader.Terms(query))).Order();
            Assert.Equal(expected, NameIndexReader.Search(fixture.Database, query, null, 100).Select(hit => hit.Name).Order());
        }
        var diagnostics = new NameSearchDiagnostics();
        Assert.Empty(NameIndexReader.Search(fixture.Database, "zz", null, 100, diagnostics: diagnostics));
        Assert.Equal(0, diagnostics.EvaluatedNames);
    }

    [Theory]
    [MemberData(nameof(SchemasAndTables))]
    public void Exact_and_stem_ranks_precede_LIMIT_even_after_many_candidates(int schema, BaseTable table)
    {
        using var fixture = new IndexFixture(schema,
            Enumerable.Range(0, 200).Select(i => $"aether-long-{i:D3}.txt").Append("aether.exe").Append("aether").ToArray(), table: table);
        var hits = NameIndexReader.Search(fixture.Database, " \tAETHER\u2003", null, 2);
        Assert.Equal(["aether", "aether.exe"], hits.Select(hit => hit.Name));
    }

    [Theory]
    [MemberData(nameof(SchemasTablesAndFlags))]
    public void Custom_rank_and_filter_are_applied_before_LIMIT_and_receive_original_arguments(int schema, BaseTable table, bool rankByPath)
    {
        using var fixture = new IndexFixture(schema,
            Enumerable.Range(0, 150).Select(i => $"aether-{i:D3}.txt").Append("aether-tool.exe").Append("aether-folder").ToArray(),
            directoryNames: ["aether-folder"], table: table);
        var rankCalls = new List<(string Value, bool Directory)>();
        var hits = NameIndexReader.Search(fixture.Database, "aether", null, 1, rank: (value, directory) =>
        {
            rankCalls.Add((value, directory));
            return value.EndsWith(".exe", StringComparison.Ordinal) ? -1 : 1;
        }, filter: (path, directory) =>
        {
            Assert.StartsWith(fixture.Root, path, StringComparison.Ordinal);
            return directory || path.EndsWith(".exe", StringComparison.Ordinal);
        }, rankByPath: rankByPath);
        Assert.Equal("aether-tool.exe", Assert.Single(hits).Name);
        Assert.Contains(rankCalls, call => call.Directory && call.Value == (rankByPath ? fixture.PathFor("aether-folder") : "aether-folder"));
        Assert.All(rankCalls, call => Assert.Equal(rankByPath, Path.IsPathFullyQualified(call.Value)));
    }

    [Theory]
    [MemberData(nameof(SchemasAndTables))]
    public void Pagination_is_stable_complete_and_disjoint_after_filtering_and_ranking(int schema, BaseTable table)
    {
        using var fixture = new IndexFixture(schema, Enumerable.Range(0, 137).Select(i => $"report-{i:D3}.txt").ToArray(), table: table);
        int Rank(string name, bool _) => name.Contains("1", StringComparison.Ordinal) ? 0 : 1;
        bool Include(string path, bool _) => !path.EndsWith("3.txt", StringComparison.Ordinal);
        var full = NameIndexReader.Search(fixture.Database, "report", null, 200, rank: Rank, filter: Include);
        var pages = new List<NameHit>();
        for (var offset = 0; offset < full.Count + 17; offset += 17)
            pages.AddRange(NameIndexReader.Search(fixture.Database, "report", null, 17, rank: Rank, filter: Include, offset: offset));
        Assert.Equal(full, pages);
        Assert.Equal(full.Count, pages.Select(hit => hit.Path).Distinct().Count());
        Assert.Equal(full.Take(17), NameIndexReader.Search(fixture.Database, "report", null, 17, rank: Rank, filter: Include, offset: -17));
    }

    [Theory]
    [MemberData(nameof(SchemasAndTables))]
    public void Cancelling_inside_SQLite_filter_interrupts_without_returning_partial_hits(int schema, BaseTable table)
    {
        using var fixture = new IndexFixture(schema, Enumerable.Range(0, 100).Select(i => $"report-{i:D3}.txt").ToArray(), table: table);
        using var cancellation = new CancellationTokenSource();
        var evaluated = 0;
        var error = Assert.ThrowsAny<OperationCanceledException>(() => NameIndexReader.Search(fixture.Database, "report", null, 20,
            cancellation.Token, filter: (_, _) => { if (++evaluated == 20) cancellation.Cancel(); return true; }));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.InRange(evaluated, 20, 21);
        Assert.Equal(20, NameIndexReader.Search(fixture.Database, "report", null, 20).Count);
    }

    [Theory]
    [MemberData(nameof(SchemasAndTables))]
    public void Default_rank_uses_filename_even_when_rankByPath_is_true(int schema, BaseTable table)
    {
        using var fixture = new IndexFixture(schema, ["report-final.txt", "annual-report.txt", "report.txt", "report"], table: table);
        var normal = NameIndexReader.Search(fixture.Database, " \treport\u2003", null, 10);
        var pathFlag = NameIndexReader.Search(fixture.Database, " \treport\u2003", null, 10, rankByPath: true);
        Assert.Equal(["report", "report.txt", "report-final.txt", "annual-report.txt"], normal.Select(hit => hit.Name));
        Assert.Equal(normal, pathFlag);
    }

    [Theory]
    [MemberData(nameof(SchemasAndTables))]
    public void Scoped_same_name_results_page_by_full_path_without_duplicates_or_adjacent_folder_leaks(int schema, BaseTable table)
    {
        string[] folders = ["scope", Path.Combine("scope", "sub"), "scope-other", "scope", Path.Combine("scope", "sub-2")];
        using var fixture = new IndexFixture(schema, ["report.txt", "report.txt", "report.txt", "report-final.txt", "report.txt"], table: table, folders: folders);
        var prefix = Path.Combine(fixture.Root, "scope") + Path.DirectorySeparatorChar;
        var expected = new[] { fixture.PathFor("report.txt", folders[0]), fixture.PathFor("report.txt", folders[1]),
            fixture.PathFor("report.txt", folders[4]) }.Order(StringComparer.Ordinal).Append(fixture.PathFor("report-final.txt", "scope")).ToArray();
        var full = NameIndexReader.Search(fixture.Database, "report", prefix, 100);
        Assert.Equal(expected, full.Select(hit => hit.Path));
        var pages = Enumerable.Range(0, 5).SelectMany(offset => NameIndexReader.Search(fixture.Database, "report", prefix, 1, offset: offset)).ToArray();
        Assert.Equal(full, pages);
        Assert.Equal(4, pages.Select(hit => hit.Path).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(pages, hit => hit.Path == fixture.PathFor("report.txt", "scope-other"));
    }

    [Theory]
    [MemberData(nameof(SchemasTablesAndFlags))]
    public void Rank_or_filter_exceptions_remain_SQLite_errors_and_do_not_return_partial_results(int schema, BaseTable table, bool throwingRank)
    {
        using var fixture = new IndexFixture(schema, ["report.txt", "report-final.txt"], table: table);
        const string marker = "deliberate-callback-failure";
        int Rank(string _, bool __) => throw new InvalidOperationException(marker);
        bool Filter(string _, bool __) => throw new InvalidOperationException(marker);
        var error = Assert.Throws<SqliteException>(() => NameIndexReader.Search(fixture.Database, "report", null, 10,
            rank: throwingRank ? Rank : null, filter: throwingRank ? null : Filter));
        Assert.Contains(marker, error.Message, StringComparison.Ordinal);
        Assert.Equal(2, NameIndexReader.Search(fixture.Database, "report", null, 10).Count);
    }

    [Fact]
    public void Empty_query_and_nonpositive_limit_keep_fast_exit_and_precancelled_query_throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Empty(NameIndexReader.Search("does-not-exist.db", " ", null, 20, cancellation.Token));
        Assert.Empty(NameIndexReader.Search("does-not-exist.db", "report", null, 0, cancellation.Token));
        Assert.Empty(NameIndexReader.Search("does-not-exist.db", "report", null, -1, cancellation.Token));
        var error = Assert.ThrowsAny<OperationCanceledException>(() => NameIndexReader.Search("does-not-exist.db", "report", null, 20, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    private sealed class IndexFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("FilesMate-search-reader-").FullName;
        public string Database => Path.Combine(Root, "search-index.db");
        public string[] Names { get; }
        public string PathFor(string name, string folder = "parent-only") => Path.Combine(Root, folder, name);

        public IndexFixture(int schema, string[] names, string[]? directoryNames = null,
            BaseTable table = BaseTable.ProductionFts, string[]? folders = null)
        {
            Names = names;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            var fileNameSql = table switch
            {
                BaseTable.Plain => "CREATE TABLE file_name(name TEXT,path TEXT,is_dir TEXT);",
                BaseTable.LegacyFts => "CREATE VIRTUAL TABLE file_name USING fts5(name,path,is_dir UNINDEXED);",
                _ => "CREATE VIRTUAL TABLE IF NOT EXISTS file_name USING fts5(name UNINDEXED, path UNINDEXED, is_dir UNINDEXED);",
            };
            // The production shape mirrors FileNameIndexService.SchemaSql, including
            // the unused-by-this-reader metadata table. Legacy and plain shapes stay
            // in the matrix to catch compatibility and query-planner differences.
            command.CommandText = "CREATE TABLE IF NOT EXISTS file_metadata(rowid INTEGER PRIMARY KEY, size INTEGER, modified INTEGER);"
                + "CREATE TABLE IF NOT EXISTS index_meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);" + fileNameSql;
            command.ExecuteNonQuery();
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO file_name VALUES($name,$path,$dir);";
            command.Parameters.AddWithValue("$name", ""); command.Parameters.AddWithValue("$path", ""); command.Parameters.AddWithValue("$dir", "0");
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                command.Parameters["$name"].Value = name;
                command.Parameters["$path"].Value = PathFor(name, folders?[i] ?? "parent-only");
                command.Parameters["$dir"].Value = directoryNames?.Contains(name, StringComparer.Ordinal) == true ? "1" : "0";
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            command.Transaction = null;
            if (schema == 2)
            {
                command.CommandText = "CREATE VIRTUAL TABLE file_name_substring USING fts5(name,content='',tokenize='trigram'); INSERT INTO file_name_substring(rowid,name) SELECT rowid,name FROM file_name;";
                command.ExecuteNonQuery();
            }
            else if (schema == 3) NameIndexReader.BuildSubstringIndex(connection, default);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
