namespace FilesMate.Core.Metadata;

/// <summary>
/// Stable identity for a filesystem item. The path is retained for display and recovery;
/// the key is used to keep metadata attached when an item is renamed or moved.
/// </summary>
public readonly record struct FileIdentity
{
    private FileIdentity(
        string stableKey,
        string normalizedPath,
        ulong? volumeSerial,
        ulong? fileId)
    {
        if (string.IsNullOrWhiteSpace(stableKey))
        {
            throw new ArgumentException("Stable key is required.", nameof(stableKey));
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("Normalized path is required.", nameof(normalizedPath));
        }

        StableKey = stableKey;
        NormalizedPath = normalizedPath;
        VolumeSerial = volumeSerial;
        FileId = fileId;
    }

    public string StableKey { get; }

    public string NormalizedPath { get; init; }

    public ulong? VolumeSerial { get; }

    public ulong? FileId { get; }

    public bool IsStable => VolumeSerial.HasValue && FileId.HasValue;

    public static FileIdentity FromStable(
        ulong volumeSerial,
        ulong fileId,
        string normalizedPath) =>
        new(
            $"file:{volumeSerial:x16}:{fileId:x16}",
            NormalizePath(normalizedPath),
            volumeSerial,
            fileId);

    public static FileIdentity FromNormalizedPath(string normalizedPath)
    {
        var path = NormalizePath(normalizedPath);
        return new($"path:{path.ToLowerInvariant()}", path, null, null);
    }

    private static string NormalizePath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Path is required.", nameof(path))
            : path.Trim().Replace('/', '\\');
}
