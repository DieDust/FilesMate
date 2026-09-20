using FilesMate.Platform.Windows.Directories;

namespace FilesMate.App.Services;

/// <summary>One bounded, cancellable size scheduler shared by visible rows.</summary>
public static class FolderSizeCache
{
    private static readonly FolderSizeService Service = new(FolderSizeWalker.Measure);

    public static event Action? SizeCached
    {
        add => Service.SizeCached += value;
        remove => Service.SizeCached -= value;
    }

    public static bool TryGet(string path, out ulong size) => Service.TryGet(path, out size);

    public static Task<ulong> GetAsync(string path, CancellationToken cancellationToken = default,
        Action<ulong>? progress = null) => Service.GetAsync(path, cancellationToken, progress);

    public static void Invalidate(string path) => Service.Invalidate(path);
}
