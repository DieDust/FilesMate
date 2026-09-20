namespace FilesMate.Core.Operations;

public enum FileConflictAction { Skip, KeepBoth, Merge, Cancel, Replace, ReplaceWithoutUndo }

public sealed record FileConflict(string Source, string Destination, bool CanMerge, string NumberedName)
{
    public bool CanReplace { get; init; }
    public bool IsSameItem { get; init; }
    public bool DestinationIsLink { get; init; }
    public FileConflictDetails? Incoming { get; init; }
    public FileConflictDetails? Existing { get; init; }
    public bool BackupUnavailable { get; init; }
    public long BackupBytes { get; init; }
    public long BackupUsedBytes { get; init; }
}
public sealed record FileConflictDetails(long Length, DateTime LastWriteTimeUtc, DateTime CreationTimeUtc, FileAttributes Attributes)
{
    public static FileConflictDetails Read(string path)
    {
        var info = new FileInfo(path);
        return new(info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, info.Attributes);
    }
}
public sealed record FileConflictChoice(FileConflictAction Action, bool ApplyToAll = false);
public delegate Task<FileConflictChoice> FileConflictResolver(FileConflict conflict, CancellationToken token);
