namespace FilesMate.App.Preview;

public enum PreviewKind
{
    Unsupported = 0,
    Image = 1,
    Text = 2,
    Pdf = 3,
    Media = 4,
    Properties = 5,
    Html = 6,
}

public abstract record PreviewResult(string Path, PreviewKind Kind)
{
    public sealed record Unsupported(string FilePath, string Reason)
        : PreviewResult(FilePath, PreviewKind.Unsupported);

    public sealed record Image(string FilePath, string ContentType)
        : PreviewResult(FilePath, PreviewKind.Image);

    public sealed record Text(string FilePath, string Content, bool IsTruncated)
        : PreviewResult(FilePath, PreviewKind.Text);

    public sealed record Pdf(string FilePath)
        : PreviewResult(FilePath, PreviewKind.Pdf);

    public sealed record Html(string FilePath, string Content)
        : PreviewResult(FilePath, PreviewKind.Html);

    public sealed record Media(string FilePath, string ContentType)
        : PreviewResult(FilePath, PreviewKind.Media);

    public sealed record Properties(
        string FilePath,
        long Length,
        DateTimeOffset? LastWriteTimeUtc,
        FileAttributes Attributes,
        bool IsDirectory = false,
        int? ChildCount = null,
        DateTimeOffset? CreationTimeUtc = null)
        : PreviewResult(FilePath, PreviewKind.Properties);
}
