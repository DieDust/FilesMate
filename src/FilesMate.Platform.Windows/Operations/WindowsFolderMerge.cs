using FilesMate.Core.Operations;

namespace FilesMate.Platform.Windows.Operations;

public sealed record FolderMergeResult(FileUndoRecord? Undo, int Skipped, IReadOnlyList<string> Errors, bool SourceRemoved);

public static class WindowsFolderMerge
{
    // Headless merge API: callers explicitly choose the conservative skip policy.
    // Interactive operations use WindowsFileTransfer with FileConflictDialog instead.
    public static FolderMergeResult Run(string source, string destination, ILocalFileOperations operations)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination), StringComparison.OrdinalIgnoreCase)
            || !IsNormalDirectory(source) || !IsNormalDirectory(destination))
            throw new IOException("Only distinct sibling folders can be merged.");
        var result = WindowsFileTransfer.RunAsync(operations, [new(source, destination)], true,
            (conflict, _) => Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Skip)))
            .GetAwaiter().GetResult();
        return new(result.Undo, result.Skipped, result.Errors, !Directory.Exists(source));
    }

    public static bool IsNormalDirectory(string path) => Directory.Exists(path)
        && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
}
