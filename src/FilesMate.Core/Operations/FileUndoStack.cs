namespace FilesMate.Core.Operations;

public sealed class FileUndoStack
{
    public const int Limit = 5;

    private readonly List<FileUndoRecord> _undo = [];
    private readonly List<FileUndoRecord> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;
    public FileUndoRecord? Latest => _undo.Count == 0 ? null : _undo[^1];
    public event EventHandler<FileUndoRecord>? Recorded;
    public event EventHandler? Changed;

    public void Push(FileUndoRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _undo.Add(record);
        foreach (var discarded in _redo) Release(discarded);
        _redo.Clear();
        if (_undo.Count > Limit)
        {
            Release(_undo[0]);
            _undo.RemoveAt(0);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        Recorded?.Invoke(this, record);
    }

    public void Clear()
    {
        foreach (var record in _undo.Concat(_redo)) Release(record);
        _undo.Clear(); _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Release(FileUndoRecord record)
    {
        foreach (var replacement in record.Replacements) replacement.Dispose();
    }

    public bool TryUndo(ILocalFileOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (_undo.Count == 0)
        {
            return false;
        }

        var record = _undo[^1];
        try { FileUndoApplier.Undo(operations, record); }
        catch (IrreversibleDeletionException) { Clear(); throw; }
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(record);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryRedo(ILocalFileOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (_redo.Count == 0)
        {
            return false;
        }

        var record = _redo[^1];
        try { FileUndoApplier.Redo(operations, record); }
        catch (IrreversibleDeletionException) { Clear(); throw; }
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(record);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
