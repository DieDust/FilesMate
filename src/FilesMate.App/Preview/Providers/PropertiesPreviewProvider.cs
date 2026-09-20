using System.IO;

namespace FilesMate.App.Preview.Providers;

public sealed class PropertiesPreviewProvider : IPreviewProvider
{
    public bool CanHandle(string path) => true;

    public Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(request.Path))
        {
            var directory = new DirectoryInfo(request.Path);
            return Task.FromResult<PreviewResult>(new PreviewResult.Properties(
                request.Path,
                0,
                directory.Exists ? directory.LastWriteTimeUtc : null,
                directory.Exists ? directory.Attributes : 0,
                IsDirectory: true,
                ChildCount: CountChildren(directory),
                CreationTimeUtc: directory.Exists ? directory.CreationTimeUtc : null));
        }

        var info = new FileInfo(request.Path);
        return Task.FromResult<PreviewResult>(new PreviewResult.Properties(
            request.Path,
            info.Exists ? info.Length : 0,
            info.Exists ? info.LastWriteTimeUtc : null,
            info.Exists ? info.Attributes : 0,
            CreationTimeUtc: info.Exists ? info.CreationTimeUtc : null));
    }

    private static int? CountChildren(DirectoryInfo directory)
    {
        try
        {
            var count = 0;
            foreach (var _ in directory.EnumerateFileSystemInfos())
            {
                count++;
                if (count > 500)
                {
                    return 501;
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
