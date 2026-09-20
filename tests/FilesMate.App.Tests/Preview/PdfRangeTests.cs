using FilesMate.App.Preview;

namespace FilesMate.App.Tests.Preview;

public sealed class PdfRangeTests
{
    [Theory]
    [InlineData("bytes=10-19", 206, 10, 10)]
    [InlineData("bytes=250-999", 206, 250, 6)]
    [InlineData("bytes=250-", 206, 250, 6)]
    [InlineData("bytes=500-600", 416, 0, 0)]
    [InlineData("bytes=20-10", 416, 0, 0)]
    [InlineData("bytes=0-5,10-15", 416, 0, 0)]
    public void Byte_ranges_return_only_the_requested_slice(string range, int status, int start, int count)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Enumerable.Range(0, 256).Select(n => (byte)n).ToArray());
            var response = PdfPreviewAssets.OpenResponse(path, range);
            using var body = response.Body;
            Assert.Equal(status, response.Status);
            Assert.Equal(count, body.Length);
            using var bytes = new MemoryStream(); body.CopyTo(bytes);
            Assert.Equal(Enumerable.Range(start, count).Select(n => (byte)n), bytes.ToArray());
            if (count > 0) { body.Position = 0; Assert.Equal(start, body.ReadByte()); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Large_documents_are_read_by_range_without_allocating_the_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var file = File.OpenWrite(path)) { file.SetLength(80 * 1024 * 1024); file.Position = file.Length - 1; file.WriteByte(42); }
            var response = PdfPreviewAssets.OpenResponse(path, "bytes=83886079-");
            using var body = response.Body;
            Assert.Equal(206, response.Status); Assert.Equal(1, body.Length); Assert.Equal(42, body.ReadByte()); Assert.Equal(-1, body.ReadByte());
            Assert.Contains("Content-Range: bytes 83886079-83886079/83886080", response.Headers);
        }
        finally { File.Delete(path); }
    }
}
