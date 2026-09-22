namespace FilesMate.Core.Operations;

public interface ILocalFileOperations
{
    public bool RequiresUndoValidation => false;

    public void CreateDirectory(string path, bool failIfExists = false);

    public void CreateEmptyFile(string path);

    public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory);

    public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory);

    public void Rename(string source, string destinationPath);

    /// <summary>Renames a file only when the filesystem can do so without copying its bytes.</summary>
    public bool TryRenameFileWithoutCopy(string source, string destinationPath) => false;

    public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null);

    public void RestoreRecycled(IReadOnlyList<string> originalPaths);

    public void RestoreRecycledItems(IReadOnlyList<RecycleItemResult> items, Action<string>? completed = null)
    {
        // A validating backend must implement exact-entry restore rather than guessing by name.
        if (RequiresUndoValidation) throw new IOException("The exact Recycle Bin entry is unavailable.");
        foreach (var item in items)
        {
            RestoreRecycled([item.OriginalPath]);
            completed?.Invoke(item.OriginalPath);
        }
    }

    public void PermanentDelete(IReadOnlyList<string> paths);

    public void ShowProperties(string path);
}
