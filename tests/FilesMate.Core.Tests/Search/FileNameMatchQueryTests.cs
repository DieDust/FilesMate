using FilesMate.Core.Search;

namespace FilesMate.Core.Tests.Search;

public sealed class FileNameMatchQueryTests
{
    [Fact]
    public void Quotes_tokens_as_fts_prefixes()
    {
        Assert.Equal("\"report\"* AND \"2024\"*", FileNameMatchQuery.ToMatchQuery("report 2024"));
        Assert.Equal(string.Empty, FileNameMatchQuery.ToMatchQuery("   "));
    }
}
