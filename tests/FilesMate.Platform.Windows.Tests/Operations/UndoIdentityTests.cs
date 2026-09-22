using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class UndoIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.UndoIdentity", Guid.NewGuid().ToString("N"));
    public UndoIdentityTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Unchanged_move_can_be_undone_and_redone_but_later_edits_stop_undo()
    {
        var source = Path.Combine(_root, "source.txt");
        var target = Path.Combine(_root, "target.txt");
        File.WriteAllText(source, "original");
        var operations = new WindowsLocalFileOperations();
        operations.Rename(source, target);
        var record = FileUndoRecord.Relocated([new(source, target)]);
        FileUndoApplier.Undo(operations, record);
        Assert.Equal("original", File.ReadAllText(source));
        Assert.False(File.Exists(target));
        FileUndoApplier.Redo(operations, record);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.False(File.Exists(source));
        File.AppendAllText(target, " edited");
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(operations, record));
        Assert.Equal("original edited", File.ReadAllText(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_keeps_a_different_file_even_when_size_and_timestamps_match(bool moved)
    {
        var path = Path.Combine(_root, "result.txt");
        File.WriteAllText(path, "original");
        var record = moved ? FileUndoRecord.Relocated([new(Path.Combine(_root, "source.txt"), path)]) : FileUndoRecord.Created([path]);
        var created = File.GetCreationTimeUtc(path);
        var written = File.GetLastWriteTimeUtc(path);
        File.Move(path, path + ".kept");
        File.WriteAllText(path, "replaced");
        File.SetCreationTimeUtc(path, created);
        File.SetLastWriteTimeUtc(path, written);
        var ops = new NoMutationOperations();
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(ops, record));
        Assert.Equal("replaced", File.ReadAllText(path));
        Assert.Equal("original", File.ReadAllText(path + ".kept"));
        Assert.Equal(0, ops.Calls);
    }

    [Fact]
    public void Undo_keeps_files_added_to_a_created_directory()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "created")).FullName;
        var record = FileUndoRecord.Created([folder]);
        File.WriteAllText(Path.Combine(folder, "user.txt"), "keep");
        var ops = new NoMutationOperations();
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(ops, record));
        Assert.Equal(0, ops.Calls);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, "user.txt")));
    }

    [Fact]
    public void Batch_validates_every_item_before_touching_the_first()
    {
        var first = Path.Combine(_root, "first.txt"); var second = Path.Combine(_root, "second.txt");
        File.WriteAllText(first, "ok"); File.WriteAllText(second, "ok");
        var record = FileUndoRecord.Copied([first, second], []);
        File.AppendAllText(second, "edited");
        var ops = new NoMutationOperations();
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(ops, record));
        Assert.Equal(0, ops.Calls);
    }

    private sealed class NoMutationOperations : ILocalFileOperations
    {
        public bool RequiresUndoValidation => true;
        public int Calls { get; private set; }
        public void CreateDirectory(string path, bool failIfExists = false) => Calls++;
        public void CreateEmptyFile(string path) => Calls++;
        public IReadOnlyList<string> Copy(IReadOnlyList<string> paths, string directory) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> paths, string directory) => throw new NotSupportedException();
        public void Rename(string source, string target) => Calls++;
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null) => Calls++;
        public void RestoreRecycled(IReadOnlyList<string> paths) => Calls++;
        public void PermanentDelete(IReadOnlyList<string> paths) => Calls++;
        public void ShowProperties(string path) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.UndoIdentity")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(_root, true);
    }
}
