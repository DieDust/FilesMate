using System.Text;
using FilesMate.App.Preview;
using FilesMate.App.Preview.Providers;

namespace FilesMate.App.Tests.Preview;

public sealed class TextPreviewEncodingTests
{
    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-32")]
    [InlineData("utf-16BE")]
    public async Task Bom_text_is_read_without_binary_false_positive(string encodingName)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await File.WriteAllTextAsync(path, "第一行\n第二行 😀", Encoding.GetEncoding(encodingName));
            var result = Assert.IsType<PreviewResult.Text>(await new TextPreviewProvider().CreateAsync(new(path, 1)));
            Assert.Equal("第一行\n第二行 😀", result.Content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Truncated_utf8_does_not_leave_partial_character()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await File.WriteAllTextAsync(path, "ab中后续", new UTF8Encoding(false));
            var result = Assert.IsType<PreviewResult.Text>(await new TextPreviewProvider().CreateAsync(new(path, 1, 4)));
            Assert.Equal("ab", result.Content);
            Assert.True(result.IsTruncated);
        }
        finally { File.Delete(path); }
    }
}
