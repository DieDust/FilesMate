using FilesMate.App.Icons;

namespace FilesMate.App.Preview.Providers;

public sealed class ImagePreviewProvider : IPreviewProvider
{
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".webp"] = "image/webp",
        [".tif"] = "image/tiff", [".tiff"] = "image/tiff", [".ico"] = "image/x-icon",
        [".heic"] = "image/heic",
    };

    public bool CanHandle(string path) => FileTypeIconCatalog.IsImagePath(path);

    public Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(request.Path);
        var type = Types.TryGetValue(extension, out var mapped) ? mapped : "image/*";
        return Task.FromResult<PreviewResult>(new PreviewResult.Image(request.Path, type));
    }
}
