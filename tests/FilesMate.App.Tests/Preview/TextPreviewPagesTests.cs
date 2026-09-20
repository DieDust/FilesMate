using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class TextPreviewPagesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100_000)]
    public void Paging_preserves_all_content_and_bounds_layout(int lines)
    {
        var source = string.Concat(Enumerable.Repeat("日志 line\r\n", lines));
        var pages = TextPreviewPages.Split(source);
        Assert.NotEmpty(pages);
        Assert.Equal(source, string.Concat(pages));
        Assert.All(pages, page => { Assert.True(page.Length <= 16_384); Assert.True(page.Count(c => c == '\n') <= 256); });
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("\r\n")]
    public void Long_lines_do_not_split_surrogates_or_newlines(string boundary)
    {
        var source = new string('x', 16_383) + boundary + new string('y', 32_768);
        var pages = TextPreviewPages.Split(source);
        Assert.Equal(source, string.Concat(pages));
        Assert.Equal(16_383, pages[0].Length);
        Assert.All(pages, page => Assert.True(page.Length <= 16_384));
    }
}
