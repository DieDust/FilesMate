using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class GroupUndoTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void FailedCycleRestoresEveryOriginalNameAndContent(int failingRename)
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-cycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new[] { "a", "b", "c" }.Select(name => Path.Combine(root, name)).ToArray();
            for (var i = 0; i < paths.Length; i++) File.WriteAllText(paths[i], i.ToString());
            var stack = new FileUndoStack();
            stack.Push(FileUndoRecord.Relocated([new(paths[1], paths[0]), new(paths[2], paths[1]), new(paths[0], paths[2])]));
            Assert.Throws<IOException>(() => stack.TryUndo(new DiskOperations { FailingRename = failingRename }));
            for (var i = 0; i < paths.Length; i++) Assert.Equal(i.ToString(), File.ReadAllText(paths[i]));
            Assert.Equal(3, Directory.GetFiles(root).Length);
            Assert.True(stack.CanUndo);
            Assert.False(stack.CanRedo);
            Assert.True(stack.TryUndo(new DiskOperations()));
            Assert.Equal("2", File.ReadAllText(paths[0]));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndoRestoresFilesButPreservesAnythingAddedToGroup(bool addUnrelatedFile)
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMate-group-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "group");
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(root, "original.txt");
            var destination = Path.Combine(folder, "original.txt");
            File.WriteAllText(destination, "original");
            var extra = Path.Combine(folder, "later.txt");
            if (addUnrelatedFile) File.WriteAllText(extra, "later");
            var stack = new FileUndoStack();
            var record = FileUndoRecord.Grouped(folder, [new(source, destination)]);
            var recorded = 0;
            stack.Recorded += (_, value) => { Assert.Same(record, value); recorded++; };
            stack.Push(record);
            Assert.Same(record, stack.Latest);
            Assert.True(stack.TryUndo(new DiskOperations()));
            Assert.Equal("original", File.ReadAllText(source));
            Assert.Equal(addUnrelatedFile, Directory.Exists(folder));
            if (addUnrelatedFile) Assert.Equal("later", File.ReadAllText(extra));
            Assert.Null(stack.Latest);
            Assert.True(stack.TryRedo(new DiskOperations()));
            Assert.Equal("original", File.ReadAllText(destination));
            Assert.False(File.Exists(source));
            Assert.Equal(1, recorded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class DiskOperations : ILocalFileOperations
    {
        public int FailingRename { get; init; }
        private int _renameCount;
        public void CreateDirectory(string path, bool failIfExists = false) => Directory.CreateDirectory(path);
        public void Rename(string source, string destinationPath)
        {
            if (++_renameCount == FailingRename) throw new IOException("Injected move failure");
            File.Move(source, destinationPath);
        }
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null) => throw new NotSupportedException("Grouping undo must never recycle unrelated data");
        public void RestoreRecycled(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
