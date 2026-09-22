namespace FilesMate.Core.Operations;

public interface ILocalFileOperations
{
    public bool RequiresUndoValidation => false;

    public void CreateDirectory(string path, bool failIfExists = false);

    public void CreateEmptyFile(string path);

    public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory);

    public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory);

    public void Rename(string source, string destinationPath);

    public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null);

    public void RestoreRecycled(IReadOnlyList<string> originalPaths);

    public void PermanentDelete(IReadOnlyList<string> paths);

    public void ShowProperties(string path);
}
