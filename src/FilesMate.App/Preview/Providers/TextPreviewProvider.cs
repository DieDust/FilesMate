using System.Text;

namespace FilesMate.App.Preview.Providers;

public sealed class TextPreviewProvider : IPreviewProvider
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".csv", ".yaml", ".yml", ".ini", ".cs", ".js", ".ts", ".tsx", ".css", ".html",
        ".py", ".cpp", ".c", ".h", ".hpp", ".java", ".rs", ".go", ".sql", ".ps1", ".bat", ".sh", ".toml", ".config", ".jsx", ".vue",
    };

    public bool CanHandle(string path) => Extensions.Contains(Path.GetExtension(path));

    public async Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        var cap = request.Normalize().MaxTextBytes;
        var buffer = new byte[cap];
        await using var stream = new FileStream(
            request.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = 0;
        while (read < cap)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, cap - read), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        var truncated = stream.Position < stream.Length;
        Encoding encoding = Encoding.UTF8;
        var skip = 0;
        if (read >= 4 && buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0 && buffer[3] == 0) { encoding = Encoding.UTF32; skip = 4; }
        else if (read >= 4 && buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xFE && buffer[3] == 0xFF) { encoding = new UTF32Encoding(true, true); skip = 4; }
        else if (read >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE) { encoding = Encoding.Unicode; skip = 2; }
        else if (read >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF) { encoding = Encoding.BigEndianUnicode; skip = 2; }
        else if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) skip = 3;
        // Do not render a replacement glyph for a partial codepoint at the cap.
        var decoder = encoding.GetDecoder();
        var chars = new char[encoding.GetMaxCharCount(read - skip)];
        var countChars = decoder.GetChars(buffer, skip, read - skip, chars, 0, flush: !truncated);
        var content = new string(chars, 0, countChars);
        if (content.Contains('\0'))
        {
            return new PreviewResult.Unsupported(request.Path, "The file contains binary data.");
        }

        return new PreviewResult.Text(request.Path, content, truncated);
    }
}
