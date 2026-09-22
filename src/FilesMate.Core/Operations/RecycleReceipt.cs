namespace FilesMate.Core.Operations;

/// <summary>The exact shell-created recycle entry, retained only for this undo session.</summary>
public sealed class RecycleReceipt(string originalPath, string dataPath, string infoPath, FileAttributes originalAttributes)
{
    public string OriginalPath { get; } = originalPath;
    public string DataPath { get; } = dataPath;
    public string InfoPath { get; } = infoPath;
    public FileAttributes OriginalAttributes { get; } = originalAttributes;
    // Restoration moves the exact root without deleting or overwriting a live destination.
    // There is no need to enumerate all descendants of a large recycled directory.
    private readonly FileUndoState _state = FileUndoState.CaptureRootsOnly([dataPath, infoPath]);

    public void Validate() => _state.Validate();
}
