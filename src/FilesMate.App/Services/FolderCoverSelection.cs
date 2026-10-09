namespace FilesMate.App.Services;

internal static class FolderCoverSelection
{
    // Keep both entry points on the same set of existing shell-thumbnail formats.
    internal static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[]
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".heic",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpeg", ".mpg",
    });

    internal static bool IsSupported(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
