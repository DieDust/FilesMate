namespace FilesMate.Core.Operations;

public sealed class FileUndoStack(IFileUndoCleanupScheduler? cleanupScheduler = null)
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
        foreach (var discarded in _redo) ScheduleRelease(discarded);
        _redo.Clear();
        TrimHistory();
        Changed?.Invoke(this, EventArgs.Empty);
        Recorded?.Invoke(this, record);
    }

    public void Clear()
    {
        // Final application shutdown must not abandon an older scheduled cleanup.
        cleanupScheduler?.Drain();
        foreach (var record in _undo.Concat(_redo)) Release(record);
        _undo.Clear(); _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Release(FileUndoRecord record)
    {
        foreach (var replacement in record.Replacements) replacement.Dispose();
    }

    private void ScheduleRelease(FileUndoRecord record)
    {
        // Capture ownership now. The worker must never inspect a subsequently changed stack.
        var replacements = record.Replacements.ToArray();
        if (replacements.Length == 0) return;
        void Cleanup() { foreach (var replacement in replacements) replacement.Dispose(); }
        if (cleanupScheduler is null) Cleanup(); else cleanupScheduler.Schedule(Cleanup);
    }

    private void TrimHistory()
    {
        while (_undo.Count + _redo.Count > Limit)
        {
            // Partial operations create independent pieces. Keep the next undo and redo
            // available, including a failed remainder; expire the farthest older piece.
            var oldest = _undo.Count > 1 ? _undo : _redo;
            ScheduleRelease(oldest[0]);
            oldest.RemoveAt(0);
        }
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
        catch (PartialFileUndoException error) { ApplyPartial(_undo, _redo, error); throw; }
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(record);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> TryApplyAsync(ILocalFileOperations operations, bool redo, Func<Action, Task> worker)
    {
        var source = redo ? _redo : _undo;
        if (source.Count == 0) return false;
        var record = source[^1];
        try
        {
            await worker(() =>
            {
                if (redo) FileUndoApplier.Redo(operations, record);
                else FileUndoApplier.Undo(operations, record);
            });
        }
        catch (IrreversibleDeletionException) { Clear(); throw; }
        catch (PartialFileUndoException error) { ApplyPartial(source, redo ? _undo : _redo, error); throw; }
        source.RemoveAt(source.Count - 1);
        (redo ? _undo : _redo).Add(record);
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
        catch (PartialFileUndoException error) { ApplyPartial(_redo, _undo, error); throw; }
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(record);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void ApplyPartial(List<FileUndoRecord> source, List<FileUndoRecord> inverse, PartialFileUndoException error)
    {
        source.RemoveAt(source.Count - 1);
        if (error.Remaining.HasActions)
            source.Add(error.Remaining);
        inverse.Add(error.Completed);
        TrimHistory();
        Changed?.Invoke(this, EventArgs.Empty);
        // Keep cancellation and error handling unchanged after committing the actual progress.
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw();
    }
}
