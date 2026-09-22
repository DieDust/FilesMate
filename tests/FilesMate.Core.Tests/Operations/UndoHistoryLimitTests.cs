using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class UndoHistoryLimitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.HistoryLimit", Guid.NewGuid().ToString("N"));
    private readonly FileUndoStack _stack = new();
    private readonly LocalOperations _operations = new();

    [Fact]
    public void Repeated_partial_cancellation_cannot_expand_history_beyond_the_limit()
    {
        Directory.CreateDirectory(_root);
        var paths = Enumerable.Range(0, 12).Select(i => Path.Combine(_root, $"item{i}.txt")).ToArray();
        foreach (var path in paths) File.WriteAllText(path, "owned");
        _stack.Push(FileUndoRecord.Created(paths));
        _operations.CancelAfterFirst = true;
        while (_stack.CanUndo)
        {
            try { _stack.TryUndo(_operations); }
            catch (OperationCanceledException) { }
        }
        var restored = 0;
        while (_stack.TryRedo(_operations)) restored++;
        Assert.Equal(FileUndoStack.Limit, restored);
        var extra = Path.Combine(_root, "extra.txt"); File.WriteAllText(extra, "extra");
        _stack.Push(FileUndoRecord.Created([extra]));
        _operations.CancelAfterFirst = false;
        var undone = 0;
        while (_stack.TryUndo(_operations)) undone++;
        Assert.Equal(FileUndoStack.Limit, undone);
    }

    [Fact]
    public void Expiring_partial_replacements_releases_only_their_owned_backups_and_budget()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_root);
        var budget = new ReplacementBackupBudget(1000, 100);
        budget.Initialize(Path.Combine(_root, "journal"));
        var replacements = new List<FileReplacement>();
        for (var i = 0; i < 7; i++)
        {
            var source = Path.Combine(_root, $"source{i}"); var destination = Path.Combine(_root, $"target{i}");
            File.WriteAllText(source, "incoming"); File.WriteAllText(destination, "incoming");
            var directory = Path.Combine(_root, ".filesmate-history-" + Guid.NewGuid().ToString("N"));
            var lease = budget.Reserve(directory, 10, () => Directory.CreateDirectory(directory));
            var backup = Path.Combine(directory, "old"); File.WriteAllText(backup, "original");
            replacements.Add(new(source, destination, backup, false, lease));
        }
        _stack.Push(FileUndoRecord.Copied([], []) with { Replacements = replacements });
        Assert.Equal(70, budget.UsedBytes);
        for (var i = 5; i >= 0; i--)
        {
            using var locked = new FileStream(replacements[i].Destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.ThrowsAny<IOException>(() => _stack.TryUndo(_operations));
            Assert.True(_stack.CanUndo); Assert.True(_stack.CanRedo);
        }
        Assert.Equal(50, budget.UsedBytes); Assert.Equal(5, budget.Entries.Count);
        Assert.True(_stack.TryUndo(_operations));
        var redone = 0;
        while (_stack.TryRedo(_operations)) redone++;
        Assert.Equal(FileUndoStack.Limit, redone);
        for (var i = 0; i < replacements.Count; i++)
        {
            Assert.Equal(i < 5 ? "incoming" : "original", File.ReadAllText(replacements[i].Destination));
            Assert.Equal("incoming", File.ReadAllText(replacements[i].Source));
        }
        _stack.Clear();
        Assert.Equal(0, budget.UsedBytes); Assert.Empty(budget.Entries);
    }

    private sealed class LocalOperations : ILocalFileOperations
    {
        public bool RequiresUndoValidation => true;
        public bool CancelAfterFirst { get; set; }
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        {
            foreach (var path in paths)
            {
                File.Move(path, path + ".recycled"); completed?.Invoke(new(path, true));
                if (CancelAfterFirst && paths.Count > 1) throw new OperationCanceledException();
            }
        }
        public void RestoreRecycledItems(IReadOnlyList<RecycleItemResult> items, Action<string>? completed = null)
        {
            foreach (var item in items) { File.Move(item.OriginalPath + ".recycled", item.OriginalPath); completed?.Invoke(item.OriginalPath); }
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
        _stack.Clear();
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.HistoryLimit")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
