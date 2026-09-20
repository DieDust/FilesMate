using System.IO;

namespace FilesMate.Core.Icons;

/// <summary>
/// Loads Windows shell icons off the UI thread. Callers must not extract icons during row realization.
/// </summary>
public interface IIconService
{
    public IconBitmap? TryGetCached(in IconKey key);

    public Task<IconBitmap?> GetAsync(
        IconKey key,
        string? path,
        FileAttributes attributes,
        bool directory,
        CancellationToken cancellationToken);
}
