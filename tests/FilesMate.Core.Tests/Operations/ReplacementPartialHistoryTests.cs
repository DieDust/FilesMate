using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class ReplacementPartialHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.ReplacementPartial", Guid.NewGuid().ToString("N"));
    private readonly FileUndoStack _stack = new();
    private readonly LocalOperations _operations = new();
    private readonly List<CountingLease> _leases = [];

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Locked_replacement_keeps_completed_work_reversible_and_pending_work_retryable(bool redo, bool move, bool reverseCompletedFirst)
    {
        if (!OperatingSystem.IsWindows()) return; // Windows sharing modes block the rename/replace.
        var (first, second) = Prepare(move);
        if (redo) Assert.True(_stack.TryUndo(_operations));
        using (var locked = new FileStream((redo ? second : first).Destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => Apply(redo));

        Assert.Equal("incoming first", File.ReadAllText(first.Destination));
        Assert.Equal("original second", File.ReadAllText(second.Destination));
        Assert.True(_stack.CanUndo); Assert.True(_stack.CanRedo);
        if (reverseCompletedFirst)
        {
            Assert.True(Apply(!redo));
            Assert.Equal(redo ? "original first" : "incoming first", File.ReadAllText(first.Destination));
            Assert.Equal(redo ? "original second" : "incoming second", File.ReadAllText(second.Destination));
            Assert.True(Apply(redo)); Assert.True(Apply(redo));
        }
        else Assert.True(Apply(redo));

        AssertVersion(first, applied: redo, move); AssertVersion(second, applied: redo, move);
        Assert.True(Apply(!redo)); Assert.True(Apply(!redo));
        AssertVersion(first, applied: !redo, move); AssertVersion(second, applied: !redo, move);
        AssertSingleOwnershipAndClear();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void External_edit_of_pending_replacement_is_never_adopted_by_partial_history(bool redo, bool move)
    {
        var (first, second) = Prepare(move);
        if (redo) Assert.True(_stack.TryUndo(_operations));
        var pending = redo ? second : first;
        var completed = redo ? first : second;
        File.WriteAllText(pending.Destination, "external edit must remain");
        Assert.Throws<UndoStateChangedException>(() => Apply(redo));
        Assert.True(_stack.CanUndo); Assert.True(_stack.CanRedo);
        Assert.True(Apply(!redo));
        AssertVersion(completed, applied: !redo, move);
        // Reapply the completed piece, then retry only the still-invalid piece.
        Assert.True(Apply(redo));
        Assert.Throws<UndoStateChangedException>(() => Apply(redo));
        Assert.Equal("external edit must remain", File.ReadAllText(pending.Destination));
        AssertSingleOwnershipAndClear();
    }

    [Fact]
    public void Redo_failure_after_restoring_an_ordinary_copy_preserves_both_histories()
    {
        if (!OperatingSystem.IsWindows()) return;
        var replacement = CreateReplacement("replacement", move: false);
        var ordinary = Path.Combine(_root, "ordinary.txt"); File.WriteAllText(ordinary, "ordinary");
        _stack.Push(FileUndoRecord.Copied([ordinary], []) with { Replacements = [replacement] });
        Assert.True(_stack.TryUndo(_operations));
        using (var locked = new FileStream(replacement.Destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => _stack.TryRedo(_operations));
        Assert.Equal("ordinary", File.ReadAllText(ordinary));
        AssertVersion(replacement, applied: false, move: false);
        Assert.True(_stack.CanUndo); Assert.True(_stack.CanRedo);
        Assert.True(_stack.TryUndo(_operations)); Assert.False(File.Exists(ordinary));
        Assert.True(_stack.TryRedo(_operations)); Assert.True(_stack.TryRedo(_operations));
        Assert.Equal("ordinary", File.ReadAllText(ordinary));
        AssertVersion(replacement, applied: true, move: false);
        AssertSingleOwnershipAndClear();
    }

    private (FileReplacement First, FileReplacement Second) Prepare(bool move)
    {
        var first = CreateReplacement("first", move); var second = CreateReplacement("second", move);
        _stack.Push(FileUndoRecord.Copied([], []) with { Replacements = [first, second] });
        return (first, second);
    }

    private FileReplacement CreateReplacement(string name, bool move)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        var source = Path.Combine(directory, "source.txt"); var destination = Path.Combine(directory, "target.txt");
        var backup = Path.Combine(Directory.CreateDirectory(Path.Combine(directory, "backup")).FullName, "old.txt");
        File.WriteAllText(destination, "incoming " + name); File.WriteAllText(backup, "original " + name);
        if (!move) File.WriteAllText(source, "incoming " + name);
        var lease = new CountingLease(); _leases.Add(lease);
        return new(source, destination, backup, move, lease);
    }

    private static void AssertVersion(FileReplacement replacement, bool applied, bool move)
    {
        var name = Path.GetFileName(Path.GetDirectoryName(replacement.Destination));
        Assert.Equal((applied ? "incoming " : "original ") + name, File.ReadAllText(replacement.Destination));
        Assert.Equal(applied, replacement.IsApplied);
        if (move) Assert.Equal(!applied, File.Exists(replacement.Source));
        if (!move || !applied) Assert.Equal("incoming " + name, File.ReadAllText(replacement.Source));
        if (!move || applied) Assert.Equal((applied ? "original " : "incoming ") + name, File.ReadAllText(replacement.Backup));
    }

    private bool Apply(bool redo) => redo ? _stack.TryRedo(_operations) : _stack.TryUndo(_operations);
    private void AssertSingleOwnershipAndClear()
    {
        Assert.All(_leases, lease => Assert.Equal(0, lease.Disposals));
        _stack.Clear(); _stack.Clear();
        Assert.All(_leases, lease => Assert.Equal(1, lease.Disposals));
    }
    private sealed class CountingLease : IDisposable { public int Disposals { get; private set; } public void Dispose() => Disposals++; }

    private sealed class LocalOperations : ILocalFileOperations
    {
        public bool RequiresUndoValidation => true;
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        {
            foreach (var path in paths) { File.Move(path, path + ".recycled"); completed?.Invoke(new(path, true)); }
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
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.ReplacementPartial")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
