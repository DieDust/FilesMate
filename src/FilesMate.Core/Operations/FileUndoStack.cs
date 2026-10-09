namespace FilesMate.Core.Operations;

public sealed class FileUndoStack(IFileUndoCleanupScheduler? cleanupScheduler = null)
{
    public const int Limit = 5;
    public const int CombinedLimit = 64;
    public bool IsApplying { get; private set; }

    private readonly List<FileUndoRecord> _undo = [];
    private readonly List<FileUndoRecord> _redo = [];

    public bool CanUndo => _undo.Count > 0 && _undo[^1].Kind != FileUndoKind.ShellTransfer;

    public bool CanRedo => _redo.Count > 0;
    public FileUndoRecord? Latest => _undo.Count == 0 ? null : _undo[^1];
    public FileUndoRecord? LatestRedo => _redo.Count == 0 ? null : _redo[^1];
    public IReadOnlyList<FileUndoRecord> UndoRecords => _undo.AsEnumerable().Reverse().ToArray();
    public IReadOnlyList<FileUndoRecord> RedoRecords => _redo.AsEnumerable().Reverse().ToArray();
    public event EventHandler<FileUndoRecord>? Recorded;
    public event EventHandler? Changed;

    public void Push(FileUndoRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (IsApplying) throw new InvalidOperationException("History is being applied.");
        _undo.Add(record);
        foreach (var discarded in _redo) ScheduleRelease(discarded);
        _redo.Clear();
        TrimHistory();
        Changed?.Invoke(this, EventArgs.Empty);
        if (record.Kind != FileUndoKind.ViewState) Recorded?.Invoke(this, record);
    }

    /// <summary>Withdraws one exact provisional view action on cancellation, preserving other chronological records.</summary>
    public bool TryDiscardPendingView(FileUndoRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (IsApplying || record.Kind != FileUndoKind.ViewState) return false;
        var index = _undo.FindIndex(item => ReferenceEquals(item, record));
        if (index < 0) return false;
        _undo.RemoveAt(index);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        if (IsApplying) throw new InvalidOperationException("History is being applied.");
        ClearCore();
    }

    private void ClearCore()
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
        static int Files(List<FileUndoRecord> records) => records.Count(r => r.Kind != FileUndoKind.ViewState);
        while (Files(_undo) + Files(_redo) > Limit)
        {
            // Partial operations create independent pieces. Keep the next undo and redo
            // available, including a failed remainder; expire the farthest older piece.
            var oldest = Files(_undo) > 1 ? _undo : _redo;
            var index = oldest.FindIndex(r => r.Kind != FileUndoKind.ViewState);
            ScheduleRelease(oldest[index]);
            oldest.RemoveAt(index);
        }
        while (_undo.Count + _redo.Count > CombinedLimit)
        {
            var oldest = _undo.Count > 1 ? _undo : _redo;
            ScheduleRelease(oldest[0]);
            oldest.RemoveAt(0);
        }
    }

    public bool TryUndo(ILocalFileOperations operations) => ApplySynchronous(operations, redo: false);

    public bool TryRedo(ILocalFileOperations operations) => ApplySynchronous(operations, redo: true);

    private bool ApplySynchronous(ILocalFileOperations operations, bool redo)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var source = redo ? _redo : _undo;
        if (IsApplying || source.Count == 0 || source[^1].Kind == FileUndoKind.ShellTransfer) return false;
        var record = source[^1];
        IsApplying = true;
        try
        {
            try { ApplyRecord(operations, record, redo); }
            catch (IrreversibleDeletionException) { ClearCore(); throw; }
            catch (PartialFileUndoException error) { ApplyPartial(source, redo ? _undo : _redo, error); throw; }
            source.RemoveAt(source.Count - 1);
            (redo ? _undo : _redo).Add(record);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally { IsApplying = false; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public async Task<bool> TryApplyAsync(ILocalFileOperations operations, bool redo, Func<Action, Task> worker)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(worker);
        var source = redo ? _redo : _undo;
        if (IsApplying || source.Count == 0 || source[^1].Kind == FileUndoKind.ShellTransfer) return false;
        var record = source[^1];
        IsApplying = true;
        try
        {
            try
            {
                // A view delta belongs to its UI owner. File operations retain the
                // existing worker and safety-check path. There is no parallel view stack.
                if (record.Kind == FileUndoKind.ViewState) ApplyRecord(operations, record, redo);
                else await worker(() => ApplyRecord(operations, record, redo));
            }
            catch (IrreversibleDeletionException) { ClearCore(); throw; }
            catch (PartialFileUndoException error) { ApplyPartial(source, redo ? _undo : _redo, error); throw; }
            source.RemoveAt(source.Count - 1);
            (redo ? _undo : _redo).Add(record);
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally { IsApplying = false; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private static void ApplyRecord(ILocalFileOperations operations, FileUndoRecord record, bool redo)
    {
        if (record.Kind == FileUndoKind.ViewState)
        {
            (record.ViewChange ?? throw new InvalidOperationException("Missing view history delta.")).Apply(redo);
            return;
        }
        if (redo) FileUndoApplier.Redo(operations, record);
        else FileUndoApplier.Undo(operations, record);
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
