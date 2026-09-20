using Loc = FilesMate.App.Localization.StringTable;
namespace FilesMate.App.Preview;

public static class PdfPreviewAssets
{
    public const string Host = "filesmate-pdf.local";
    public const string ViewerUri = "https://filesmate-pdf.local/index.html";
    public const string DocumentUri = "https://filesmate-document.local/document.pdf";
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "PdfPreview");
    public static Stream OpenDocument(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= 2L * 1024 * 1024 * 1024) return stream;
        stream.Dispose();
        throw new IOException(Loc.Get("Preview_PdfTooLarge"));
    }

    public static (Stream Body, int Status, string Headers) OpenResponse(string path, string? range)
    {
        var file = OpenDocument(path);
        var length = file.Length;
        const string headers = "Content-Type: application/pdf\r\nCache-Control: no-store\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: https://filesmate-pdf.local\r\nAccess-Control-Expose-Headers: Content-Length, Content-Range, Accept-Ranges\r\n";
        if (string.IsNullOrEmpty(range)) return (file, 200, headers + $"Content-Length: {length}");
        var parts = range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) ? range[6..].Split('-') : [];
        if (parts.Length != 2 || !long.TryParse(parts[0], out var start) || start < 0 || start >= length ||
            (parts[1].Length > 0 && (!long.TryParse(parts[1], out var requestedEnd) || requestedEnd < start)))
        {
            file.Dispose();
            return (new MemoryStream(), 416, headers + $"Content-Range: bytes */{length}\r\nContent-Length: 0");
        }
        var end = parts[1].Length == 0 ? length - 1 : Math.Min(long.Parse(parts[1]), length - 1);
        return (new FileRangeStream(file, start, end - start + 1), 206, headers + $"Content-Range: bytes {start}-{end}/{length}\r\nContent-Length: {end - start + 1}");
    }

    private sealed class FileRangeStream(Stream source, long start, long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            source.Position = start + _position;
            var read = source.Read(buffer, offset, (int)Math.Min(count, Math.Max(0, length - _position)));
            _position += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, SeekOrigin.End => length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (position < 0) throw new IOException("Invalid PDF byte range");
            return _position = position;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
}
