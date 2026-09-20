namespace FilesMate.App.Preview.Providers;

public sealed class MediaPreviewProvider : IPreviewProvider
{
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".flac"] = "audio/flac", [".m4a"] = "audio/mp4",
        [".mp4"] = "video/mp4", [".mkv"] = "video/x-matroska", [".avi"] = "video/x-msvideo", [".mov"] = "video/quicktime", [".webm"] = "video/webm",
    };

    public bool CanHandle(string path) => Types.ContainsKey(Path.GetExtension(path));

    public Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(request.Path);
        return Task.FromResult<PreviewResult>(new PreviewResult.Media(request.Path, Types[extension]));
    }
}
