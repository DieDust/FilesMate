using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class WindowsCompoundFolderTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.CompoundTransfer", Guid.NewGuid().ToString("N"));
    private readonly WindowsLocalFileOperations _operations = new();
    private readonly FileUndoStack _stack = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_named_folders_with_a_later_nested_child_merge_round_trip(bool destinationExists)
    {
        if (!OperatingSystem.IsWindows()) return; // Native transfer construction and file identities require Windows.
        var first = Directory.CreateDirectory(Path.Combine(_root, "in1", "same")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "in2", "same")).FullName;
        var target = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "out")).FullName, "same");
        File.WriteAllText(Path.Combine(first, "a.txt"), "first");
        Directory.CreateDirectory(Path.Combine(second, "nested", "deep"));
        var child = Path.Combine("nested", "deep", "b.txt");
        File.WriteAllText(Path.Combine(second, child), "second");
        if (destinationExists) { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep.txt"), "existing"); }
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(first, target), new(second, target)], true,
            (conflict, _) => Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Cancel)));
        Assert.Empty(result.Errors); Assert.False(result.Cancelled); Assert.NotNull(result.Undo);
        _stack.Push(result.Undo);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Assert.True(_stack.TryUndo(_operations));
            Assert.Equal("first", File.ReadAllText(Path.Combine(first, "a.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(second, child)));
            if (destinationExists) Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "keep.txt")));
            else Assert.False(Directory.Exists(target));
            Assert.True(_stack.TryRedo(_operations));
            Assert.False(Directory.Exists(first)); Assert.False(Directory.Exists(second));
            Assert.Equal("first", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(target, child)));
            if (destinationExists) Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "keep.txt")));
            Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).StartsWith(".filesmate-undo-", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_named_folders_with_nested_replacements_round_trip(bool destinationExists)
    {
        if (!OperatingSystem.IsWindows()) return; // Native replacement, leases and identity validation are Windows gates.
        var first = Directory.CreateDirectory(Path.Combine(_root, "in1", "same")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "in2", "same")).FullName;
        var target = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "out")).FullName, "same");
        File.WriteAllText(Path.Combine(first, "a.txt"), "first"); File.WriteAllText(Path.Combine(second, "a.txt"), "second");
        if (destinationExists) { Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "a.txt"), "existing"); }
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(first, target), new(second, target)], true,
            (conflict, _) => Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Replace)),
            backupBudget: new ReplacementBackupBudget());
        Assert.Empty(result.Errors); Assert.False(result.Cancelled); Assert.NotNull(result.Undo);
        _stack.Push(result.Undo);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Assert.True(_stack.TryUndo(_operations));
            Assert.Equal("first", File.ReadAllText(Path.Combine(first, "a.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(second, "a.txt")));
            if (destinationExists) Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "a.txt")));
            else Assert.False(Directory.Exists(target));
            Assert.True(_stack.TryRedo(_operations));
            Assert.False(Directory.Exists(first)); Assert.False(Directory.Exists(second));
            Assert.Equal("second", File.ReadAllText(Path.Combine(target, "a.txt")));
        }
    }

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("FILESMATE_ACCEPTANCE_KEEP_FIXTURES") == "1") return;
        _stack.Clear();
        var boundary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.CompoundTransfer")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(boundary, StringComparison.Ordinal) && Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
