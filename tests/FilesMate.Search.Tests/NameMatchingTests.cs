using System.Collections.ObjectModel;
using System.Globalization;
using FilesMate.Search;

namespace FilesMate.Search.Tests;

public sealed class NameMatchingTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(" \t\r\n\u2003", "")]
    [InlineData(" a\tB\r\n文档\u2003_100% ", "a|B|文档|_100%")]
    public void Terms_preserve_literal_text_and_split_all_whitespace(string query, string expected) =>
        Assert.Equal(expected, string.Join('|', NameIndexReader.Terms(query)));

    [Fact]
    public void Matching_preserves_AND_ordinal_case_and_literal_semantics_for_list_shapes()
    {
        string[] names = ["", "a", "AetherSwap.exe", "MyAetherSwap.exe", "report_100%.txt", "reportA100X.txt",
            "中文测试文档.txt", "été.txt", "İstanbul.txt", "Straße.txt", "𐐨-report.txt", "a[b]*?\".txt"];
        string[] queries = ["", " ", "a", "ae", "aether", "swap exe", "swap missing", "文", "文档", "中文 文档",
            "ÉTÉ", "i", "STRASSE", "𐐀", "_100%", "[b]", "*?\"", "\ud801", "\udc28"];
        foreach (var name in names)
        foreach (var query in queries)
        {
            var terms = NameIndexReader.Terms(query);
            var expected = LegacyMatches(name, terms);
            Assert.Equal(expected, NameIndexReader.Matches(name, terms));
            Assert.Equal(expected, NameIndexReader.Matches(name, terms.ToList()));
            Assert.Equal(expected, NameIndexReader.Matches(name, new ReadOnlyCollection<string>(terms)));
        }
    }

    [Fact]
    public void Matching_preserves_empty_terms_and_short_circuiting()
    {
        Assert.False(NameIndexReader.Matches(null!, []));
        Assert.True(NameIndexReader.Matches("anything", [""]));
        Assert.False(NameIndexReader.Matches("anything", ["missing", null!]));
        Assert.Throws<ArgumentNullException>(() => NameIndexReader.Matches("anything", [null!]));
        Assert.Throws<NullReferenceException>(() => NameIndexReader.Matches(null!, ["a"]));
    }

    [Theory]
    [InlineData("report", "report", 0)]
    [InlineData("report.txt", "report", 1)]
    [InlineData("report-final.txt", "report", 2)]
    [InlineData("annual-report.txt", "report", 3)]
    [InlineData("REPORT.txt", " \tReport\u2003", 1)]
    [InlineData("report.tar.gz", "report.tar", 1)]
    [InlineData("report.", "report", 1)]
    [InlineData(".gitignore", "", 1)]
    [InlineData("", " ", 0)]
    public void Relevance_retains_all_rank_bands(string name, string query, int expected) =>
        Assert.Equal(expected, NameIndexReader.Relevance(name, query));

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void Relevance_matches_original_for_paths_extensions_unicode_and_whitespace(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string[] names = ["", ".", "..", ".gitignore", "archive.tar.gz", "report", "report.", "REPORT.txt",
                "report-final.txt", "annual-report.txt", "文档.txt", "été.txt", "İstanbul.txt", "𐐨.txt",
                Path.Combine("parent", "report.txt"), Path.Combine("parent.with.dots", "report"),
                "C:\\parent\\report.txt", "folder/", "folder\\", "report.txt "];
            string[] queries = ["", " ", "report", "REPORT", " \treport\u2003", "report.txt", ".gitignore", "archive.tar",
                "文档", "ÉTÉ", "i", "𐐀", "report.", "parent", "folder", "\u00a0report\u00a0"];
            foreach (var name in names)
            foreach (var query in queries)
                Assert.Equal(LegacyRelevance(name, query), NameIndexReader.Relevance(name, query));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Relevance_preserves_existing_null_argument_failure()
    {
        Assert.Throws<NullReferenceException>(() => NameIndexReader.Relevance(null!, "report"));
        Assert.Throws<NullReferenceException>(() => NameIndexReader.Relevance("report", null!));
    }

    [Fact]
    public void Matching_and_unpadded_relevance_do_not_allocate_per_candidate()
    {
        const string name = "annual-report.txt", query = "report";
        string[] terms = ["report", "txt"];
        for (var i = 0; i < 1000; i++)
        {
            NameIndexReader.Matches(name, terms);
            NameIndexReader.Relevance(name, query);
        }
        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            if (NameIndexReader.Matches(name, terms)) checksum++;
            checksum += NameIndexReader.Relevance(name, query);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(40_000, checksum);
        Assert.Equal(0, allocated);
    }

    internal static bool LegacyMatches(string name, IReadOnlyList<string> terms) =>
        terms.Count > 0 && terms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));

    internal static int LegacyRelevance(string name, string query) =>
        name.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 0
        : Path.GetFileNameWithoutExtension(name).Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 1
        : name.StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase) ? 2 : 3;
}
