using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class FileUndoStackTests
{
    private sealed class ViewChange(Action<bool> apply) : IViewUndoChange
    {
        public string Description => "Recursive expansion";
        public void Apply(bool redo) => apply(redo);
    }

    [Fact]
    public void View_and_file_undo_follow_the_same_actual_action_order()
    {
        var stack = new FileUndoStack(); var ops = new MemoryOperations();
        const string first = @"D:\fixture\first.txt", second = @"D:\fixture\second.txt";
        ops.Live.Add(first); stack.Push(FileUndoRecord.Created([first]));
        var expanded = true;
        stack.Push(FileUndoRecord.View(new ViewChange(redo => expanded = redo)));
        ops.Live.Add(second); stack.Push(FileUndoRecord.Created([second]));
        Assert.True(stack.TryUndo(ops)); Assert.True(expanded); Assert.DoesNotContain(second, ops.Live);
        Assert.True(stack.TryUndo(ops)); Assert.False(expanded); Assert.Contains(first, ops.Live);
        Assert.True(stack.TryUndo(ops)); Assert.DoesNotContain(first, ops.Live);
        Assert.True(stack.TryRedo(ops)); Assert.Contains(first, ops.Live); Assert.False(expanded);
        Assert.True(stack.TryRedo(ops)); Assert.True(expanded); Assert.DoesNotContain(second, ops.Live);
        Assert.True(stack.TryRedo(ops)); Assert.Contains(second, ops.Live);
    }

    [Fact]
    public void Invalid_view_history_remains_a_boundary_before_older_file_undo()
    {
        var stack = new FileUndoStack(); var ops = new MemoryOperations();
        const string old = @"D:\fixture\unrelated.txt";
        ops.Live.Add(old); var file = FileUndoRecord.Created([old]); stack.Push(file);
        var stale = FileUndoRecord.View(new ViewChange(_ => throw new InvalidOperationException("View target no longer exists")));
        stack.Push(stale);
        for (var i = 0; i < 3; i++)
        {
            Assert.Throws<InvalidOperationException>(() => stack.TryUndo(ops));
            Assert.Same(stale, stack.Latest); Assert.Contains(old, ops.Live); Assert.False(stack.CanRedo);
        }
        Assert.Contains(file, stack.UndoRecords);
    }

    [Fact]
    public void Repeated_view_actions_have_a_finite_combined_history()
    {
        var stack = new FileUndoStack(); var ops = new MemoryOperations();
        for (var i = 0; i < 256; i++) stack.Push(FileUndoRecord.View(new ViewChange(_ => { })));
        Assert.Equal(FileUndoStack.CombinedLimit, stack.UndoRecords.Count);
        for (var i = 0; i < 20; i++) Assert.True(stack.TryUndo(ops));
        Assert.Equal(FileUndoStack.CombinedLimit, stack.UndoRecords.Count + stack.RedoRecords.Count);
        stack.Push(FileUndoRecord.View(new ViewChange(_ => { })));
        Assert.Empty(stack.RedoRecords);
        Assert.True(stack.UndoRecords.Count <= FileUndoStack.CombinedLimit);
    }

    [Fact]
    public async Task Shell_transfer_boundary_keeps_earlier_history_without_undoing_unrelated_files()
    {
        var stack = new FileUndoStack();
        var operations = new MemoryOperations();
        const string old = @"D:\fixture\earlier.txt";
        operations.Live.Add(old);
        var earlier = FileUndoRecord.Created([old]);
        stack.Push(earlier);
        stack.Push(FileUndoRecord.ShellTransfer(@"D:\destination"));
        Assert.False(stack.CanUndo);
        Assert.False(stack.TryUndo(operations));
        var workerCalled = false;
        Assert.False(await stack.TryApplyAsync(operations, false, action =>
        {
            workerCalled = true; action(); return Task.CompletedTask;
        }));
        Assert.False(workerCalled);
        Assert.Contains(old, operations.Live);
        Assert.Contains(earlier, stack.UndoRecords);
        Assert.False(stack.CanRedo);

        const string newer = @"D:\fixture\later.txt";
        operations.Live.Add(newer);
        stack.Push(FileUndoRecord.Created([newer]));
        Assert.True(stack.TryUndo(operations));
        Assert.DoesNotContain(newer, operations.Live);
        Assert.False(stack.CanUndo);
        Assert.True(stack.TryRedo(operations));
        Assert.Contains(newer, operations.Live);
        Assert.Contains(old, operations.Live);
    }

    [Fact]
    public void History_snapshots_track_undo_redo_and_expiration_without_mutable_aliases()
    {
        var stack = new FileUndoStack();
        var ops = new MemoryOperations();
        var records = Enumerable.Range(0, 6).Select(i => FileUndoRecord.Created([@"D:\fixture\" + i + ".txt"])).ToArray();
        foreach (var record in records) { ops.Live.Add(record.Paths[0]); stack.Push(record); }
        var before = stack.UndoRecords;
        Assert.Equal(5, before.Count);
        Assert.Same(records[^1], before[0]);
        Assert.DoesNotContain(records[0], before);
        Assert.True(stack.TryUndo(ops));
        Assert.Same(records[^1], stack.LatestRedo);
        Assert.Single(stack.RedoRecords);
        Assert.Equal(5, before.Count);
        Assert.Equal(4, stack.UndoRecords.Count);
        Assert.True(stack.TryRedo(ops));
        Assert.Empty(stack.RedoRecords);
        Assert.Same(records[^1], stack.Latest);
        stack.Clear();
        Assert.Empty(stack.UndoRecords);
        Assert.Empty(stack.RedoRecords);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PermanentDeletionDuringUndoOrRedoNeverOffersAnImpossibleInverse(bool redo)
    {
        var ops = new MemoryOperations { PermanentRecycle = true };
        const string path = @"D:\inbox\large.bin";
        var stack = new FileUndoStack();
        if (redo)
        {
            ops.Bin.Add(path);
            stack.Push(FileUndoRecord.Recycled([path]));
            Assert.True(stack.TryUndo(ops));
        }
        else
        {
            ops.Live.Add(path);
            stack.Push(FileUndoRecord.Created([path]));
        }
        var error = Assert.Throws<IrreversibleDeletionException>(() => redo ? stack.TryRedo(ops) : stack.TryUndo(ops));
        Assert.Equal(1, error.Count);
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        Assert.Empty(ops.Live);
        Assert.Empty(ops.Bin);
    }
    [Fact]
    public void CancelledRecycleDoesNotAdvanceUndoHistory()
    {
        var ops = new MemoryOperations { CancelRecycle = true };
        var path = @"D:\inbox\kept.txt";
        ops.Live.Add(path);
        var stack = new FileUndoStack();
        var record = FileUndoRecord.Created([path]);
        stack.Push(record);
        Assert.Throws<OperationCanceledException>(() => stack.TryUndo(ops));
        Assert.Same(record, stack.Latest);
        Assert.False(stack.CanRedo);
        Assert.Contains(path, ops.Live);
    }
    [Fact]
    public void Case_only_rename_can_be_undone_and_redone()
    {
        var ops = new MemoryOperations();
        var source = @"D:\inbox\Report.txt";
        var target = @"D:\inbox\report.txt";
        ops.Live.Add(target);
        var record = FileUndoRecord.Relocated([new FilePathPair(source, target)]);
        FileUndoApplier.Undo(ops, record);
        Assert.Equal(source, Assert.Single(ops.Live));
        FileUndoApplier.Redo(ops, record);
        Assert.Equal(target, Assert.Single(ops.Live));
    }
    [Fact]
    public void Rename_undo_and_redo_swap_the_live_path()
    {
        var ops = new MemoryOperations();
        var stack = new FileUndoStack();
        ops.Live.Add(@"D:\inbox\draft.txt");
        stack.Push(FileUndoRecord.Relocated([new FilePathPair(@"D:\inbox\draft.txt", @"D:\inbox\final.txt")]));
        ops.Rename(@"D:\inbox\draft.txt", @"D:\inbox\final.txt");

        Assert.True(stack.TryUndo(ops));
        Assert.Contains(@"D:\inbox\draft.txt", ops.Live);
        Assert.DoesNotContain(@"D:\inbox\final.txt", ops.Live);

        Assert.True(stack.TryRedo(ops));
        Assert.Contains(@"D:\inbox\final.txt", ops.Live);
        Assert.DoesNotContain(@"D:\inbox\draft.txt", ops.Live);
    }

    [Fact]
    public void Created_undo_recycles_and_redo_restores()
    {
        var ops = new MemoryOperations();
        var stack = new FileUndoStack();
        ops.Live.Add(@"D:\inbox\New folder");
        stack.Push(FileUndoRecord.Created([@"D:\inbox\New folder"]));

        Assert.True(stack.TryUndo(ops));
        Assert.DoesNotContain(@"D:\inbox\New folder", ops.Live);
        Assert.Contains(@"D:\inbox\New folder", ops.Bin);

        Assert.True(stack.TryRedo(ops));
        Assert.Contains(@"D:\inbox\New folder", ops.Live);
        Assert.DoesNotContain(@"D:\inbox\New folder", ops.Bin);
    }

    [Fact]
    public void Recycle_undo_restores_and_a_new_action_clears_redo()
    {
        var ops = new MemoryOperations();
        var stack = new FileUndoStack();
        ops.Live.Add(@"D:\inbox\gone.txt");
        stack.Push(FileUndoRecord.Recycled([@"D:\inbox\gone.txt"]));
        ops.Recycle([@"D:\inbox\gone.txt"]);

        Assert.True(stack.TryUndo(ops));
        Assert.Contains(@"D:\inbox\gone.txt", ops.Live);

        ops.Live.Add(@"D:\inbox\other.txt");
        stack.Push(FileUndoRecord.Created([@"D:\inbox\other.txt"]));

        Assert.False(stack.TryRedo(ops));
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Empty_stack_is_a_no_op()
    {
        var ops = new MemoryOperations();
        var stack = new FileUndoStack();
        Assert.False(stack.TryUndo(ops));
        Assert.False(stack.TryRedo(ops));
    }

    [Fact]
    public void Swap_rename_undo_uses_two_phase_relocate()
    {
        var ops = new MemoryOperations();
        var stack = new FileUndoStack();
        ops.Live.Add(@"D:\inbox\a.txt");
        ops.Live.Add(@"D:\inbox\b.txt");
        stack.Push(FileUndoRecord.Relocated(
        [
            new FilePathPair(@"D:\inbox\a.txt", @"D:\inbox\b.txt"),
            new FilePathPair(@"D:\inbox\b.txt", @"D:\inbox\a.txt"),
        ]));
        ops.Rename(@"D:\inbox\a.txt", @"D:\inbox\a.txt.filesmate-swap");
        ops.Rename(@"D:\inbox\b.txt", @"D:\inbox\a.txt");
        ops.Rename(@"D:\inbox\a.txt.filesmate-swap", @"D:\inbox\b.txt");

        Assert.True(stack.TryUndo(ops));
        Assert.Contains(@"D:\inbox\a.txt", ops.Live);
        Assert.Contains(@"D:\inbox\b.txt", ops.Live);
        Assert.Equal(2, ops.Live.Count);
        Assert.DoesNotContain(ops.Live, path => path.Contains("filesmate-undo", StringComparison.Ordinal));
    }

    private sealed class MemoryOperations : ILocalFileOperations
    {
        public bool CancelRecycle { get; init; }
        public bool PermanentRecycle { get; init; }
        public HashSet<string> Live { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Bin { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void CreateDirectory(string path, bool failIfExists = false)
        {
        }

        public void CreateEmptyFile(string path) => Live.Add(path);

        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory) =>
            throw new NotSupportedException();

        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory) =>
            throw new NotSupportedException();

        public void Rename(string source, string destinationPath)
        {
            Assert.True(Live.Remove(source), $"Missing source {source}");
            Assert.DoesNotContain(destinationPath, Live);
            Live.Add(destinationPath);
        }

        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        {
            if (CancelRecycle) throw new OperationCanceledException();
            foreach (var path in paths)
            {
                Assert.True(Live.Remove(path), $"Missing live path {path}");
                if (!PermanentRecycle) Bin.Add(path);
                completed?.Invoke(new(path, !PermanentRecycle));
            }
        }

        public void RestoreRecycled(IReadOnlyList<string> originalPaths)
        {
            foreach (var path in originalPaths)
            {
                Assert.True(Bin.Remove(path), $"Missing recycled path {path}");
                Live.Add(path);
            }
        }

        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();

        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
