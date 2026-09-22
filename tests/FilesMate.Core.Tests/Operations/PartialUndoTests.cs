using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class PartialUndoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.PartialUndo", Guid.NewGuid().ToString("N"));
    private readonly LocalOperations _operations = new();
    private readonly FileUndoStack _stack = new();

    [Theory]
    [InlineData(FileUndoKind.Created)]
    [InlineData(FileUndoKind.Copied)]
    [InlineData(FileUndoKind.Recycled)]
    public async Task Cancelled_recycle_keeps_completed_items_reversible_and_remaining_items_retryable(FileUndoKind kind)
    {
        var paths = Prepare(kind);
        var redo = kind == FileUndoKind.Recycled;
        if (redo) _stack.TryUndo(_operations);
        _operations.CancelNextRecycle = true;
        await Assert.ThrowsAsync<OperationCanceledException>(() => _stack.TryApplyAsync(_operations, redo, action => Task.Run(action)));
        Assert.False(File.Exists(paths[0])); Assert.True(File.Exists(paths[1]));
        Assert.True(_stack.CanUndo); Assert.True(_stack.CanRedo);
        // The opposite action handles only the completed file, not the untouched remainder.
        if (redo) _stack.TryUndo(_operations); else _stack.TryRedo(_operations);
        Assert.Equal("first", File.ReadAllText(paths[0]));
        Assert.Equal("second", File.ReadAllText(paths[1]));
        if (redo) { _stack.TryRedo(_operations); _stack.TryRedo(_operations); }
        else { _stack.TryUndo(_operations); _stack.TryUndo(_operations); }
        Assert.False(File.Exists(paths[0])); Assert.False(File.Exists(paths[1]));
    }

    [Theory]
    [InlineData(FileUndoKind.Created)]
    [InlineData(FileUndoKind.Copied)]
    [InlineData(FileUndoKind.Recycled)]
    public void Interrupted_restore_tracks_the_file_already_restored(FileUndoKind kind)
    {
        var paths = Prepare(kind);
        var redo = kind != FileUndoKind.Recycled;
        if (redo) _stack.TryUndo(_operations);
        _operations.CancelNextRestore = true;
        Assert.Throws<OperationCanceledException>(() => redo ? _stack.TryRedo(_operations) : _stack.TryUndo(_operations));
        Assert.True(File.Exists(paths[0])); Assert.False(File.Exists(paths[1]));
        Assert.True(_stack.CanUndo); Assert.True(_stack.CanRedo);
        if (redo) _stack.TryUndo(_operations); else _stack.TryRedo(_operations);
        Assert.False(File.Exists(paths[0])); Assert.False(File.Exists(paths[1]));
        if (redo) { _stack.TryRedo(_operations); _stack.TryRedo(_operations); }
        else { _stack.TryUndo(_operations); _stack.TryUndo(_operations); }
        Assert.Equal("first", File.ReadAllText(paths[0]));
        Assert.Equal("second", File.ReadAllText(paths[1]));
    }

    [Fact]
    public void Splitting_history_does_not_recapture_an_external_edit_of_a_remaining_file()
    {
        var paths = Prepare(FileUndoKind.Created);
        _operations.BeforeCancel = () => File.WriteAllText(paths[1], "external change");
        _operations.CancelNextRecycle = true;
        Assert.Throws<OperationCanceledException>(() => _stack.TryUndo(_operations));
        Assert.Throws<UndoStateChangedException>(() => _stack.TryUndo(_operations));
        Assert.Equal("external change", File.ReadAllText(paths[1]));
        Assert.True(_stack.TryRedo(_operations));
        Assert.Equal("first", File.ReadAllText(paths[0]));
    }

    private string[] Prepare(FileUndoKind kind)
    {
        Directory.CreateDirectory(_root);
        var paths = new[] { Path.Combine(_root, "first.txt"), Path.Combine(_root, "second.txt") };
        File.WriteAllText(paths[0], "first"); File.WriteAllText(paths[1], "second");
        FileUndoRecord record;
        if (kind == FileUndoKind.Recycled)
        {
            var items = new List<RecycleItemResult>();
            _operations.Recycle(paths, items.Add); record = FileUndoRecord.RecycledWithReceipts(items);
        }
        else record = kind == FileUndoKind.Copied ? FileUndoRecord.Copied(paths, []) : FileUndoRecord.Created(paths);
        _stack.Push(record);
        return paths;
    }

    [Fact]
    public void Cancelled_mixed_copy_and_replacement_keeps_the_restored_version_retryable()
    {
        var paths = Prepare(FileUndoKind.Created); _stack.Clear();
        var staged = Path.Combine(_root, "incoming"); var backupDirectory = Directory.CreateDirectory(Path.Combine(_root, "backup")).FullName;
        var backup = Path.Combine(backupDirectory, "version.txt"); File.WriteAllText(staged, "replacement");
        File.Replace(staged, paths[1], backup);
        var replacement = new FileReplacement(staged, paths[1], backup, false);
        _stack.Push(FileUndoRecord.Copied(paths, []) with { Replacements = [replacement] });
        _operations.CancelNextRecycle = true;
        Assert.Throws<OperationCanceledException>(() => _stack.TryUndo(_operations));
        Assert.Equal("second", File.ReadAllText(paths[1]));
        Assert.True(_stack.TryUndo(_operations));
        Assert.False(File.Exists(paths[1]));
        Assert.True(_stack.TryRedo(_operations)); Assert.True(_stack.TryRedo(_operations));
        Assert.Equal("first", File.ReadAllText(paths[0])); Assert.Equal("replacement", File.ReadAllText(paths[1]));
        _stack.Clear();
    }

    private sealed class LocalOperations : ILocalFileOperations
    {
        public bool RequiresUndoValidation => true;
        public bool CancelNextRecycle { get; set; }
        public bool CancelNextRestore { get; set; }
        public Action? BeforeCancel { get; set; }
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        {
            foreach (var path in paths)
            {
                File.Move(path, path + ".recycled"); completed?.Invoke(new(path, true));
                if (CancelNextRecycle) { CancelNextRecycle = false; BeforeCancel?.Invoke(); throw new OperationCanceledException(); }
            }
        }
        public void RestoreRecycledItems(IReadOnlyList<RecycleItemResult> items, Action<string>? completed = null)
        {
            foreach (var item in items)
            {
                File.Move(item.OriginalPath + ".recycled", item.OriginalPath); completed?.Invoke(item.OriginalPath);
                if (CancelNextRestore) { CancelNextRestore = false; throw new OperationCanceledException(); }
            }
        }
        public void CreateDirectory(string path, bool failIfExists = false) => Directory.CreateDirectory(path);
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> paths, string directory) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> paths, string directory) => throw new NotSupportedException();
        public void Rename(string source, string destination) => File.Move(source, destination);
        public void RestoreRecycled(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.PartialUndo")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
