using FilesMate.Core.IO;

namespace FilesMate.Core.Tests.Operations;

public sealed class PreviewOutputLimitTests
{
    [Fact]
    public void Quota_is_checked_before_appending_beyond_the_byte_limit()
    {
        using var output = new MemoryStream();
        using var bounded = new BoundedWriteStream(output, 6);
        bounded.Write(System.Text.Encoding.UTF8.GetBytes("中文"));
        Assert.Throws<IOException>(() => bounded.WriteByte(1));
        Assert.Equal(6, output.Length);
        Assert.Equal("中文", System.Text.Encoding.UTF8.GetString(output.ToArray()));
    }
}
