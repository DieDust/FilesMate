using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class WindowsFolderMergeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-merge-" + Guid.NewGuid().ToString("N"));
    private readonly WindowsLocalFileOperations _operations = new();
    private string Source => Path.Combine(_root, "new");
    private string Target => Path.Combine(_root, "existing");

    public WindowsFolderMergeTests() { Directory.CreateDirectory(Source); Directory.CreateDirectory(Target); }

    [Fact]
    public void Empty_new_folder_merges_without_touching_existing_contents_and_can_be_undone()
    {
        Write(Target, "existing.txt", "keep");
        var result = WindowsFolderMerge.Run(Source, Target, _operations);
        Assert.True(result.SourceRemoved);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.Skipped);
        Assert.False(Directory.Exists(Source));
        FileUndoApplier.Undo(_operations, result.Undo!);
        Assert.True(Directory.Exists(Source));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Target, "existing.txt")));
        FileUndoApplier.Redo(_operations, result.Undo!);
        Assert.False(Directory.Exists(Source));
        Assert.True(Directory.Exists(Target));
    }

    [Fact]
    public void Nested_merge_skips_duplicate_files_and_undo_redo_preserves_unrelated_content()
    {
        Write(Source, "shared/duplicate.txt", "source version");
        Write(Target, "shared/duplicate.txt", "destination version");
        Write(Source, "shared/new.txt", "new");
        Write(Source, "only-source/child.txt", "child");
        Write(Source, "empty-shared/leaf.txt", "leaf");
        Directory.CreateDirectory(Path.Combine(Target, "empty-shared"));
        var result = WindowsFolderMerge.Run(Source, Target, _operations);
        Assert.False(result.SourceRemoved);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(result.Errors);
        Assert.Equal("source version", File.ReadAllText(Path.Combine(Source, "shared/duplicate.txt")));
        Assert.Equal("destination version", File.ReadAllText(Path.Combine(Target, "shared/duplicate.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Target, "shared/new.txt")));
        Write(Target, "shared/later.txt", "keep later");
        FileUndoApplier.Undo(_operations, result.Undo!);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Source, "shared/new.txt")));
        Assert.Equal("child", File.ReadAllText(Path.Combine(Source, "only-source/child.txt")));
        Assert.Equal("keep later", File.ReadAllText(Path.Combine(Target, "shared/later.txt")));
        FileUndoApplier.Redo(_operations, result.Undo!);
        Assert.Equal("leaf", File.ReadAllText(Path.Combine(Target, "empty-shared/leaf.txt")));
        Assert.Equal("source version", File.ReadAllText(Path.Combine(Source, "shared/duplicate.txt")));
    }

    [Fact]
    public void Redo_empty_merge_does_not_delete_files_added_since_undo()
    {
        var result = WindowsFolderMerge.Run(Source, Target, _operations);
        FileUndoApplier.Undo(_operations, result.Undo!);
        Write(Source, "later.txt", "keep");
        FileUndoApplier.Redo(_operations, result.Undo!);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Source, "later.txt")));
    }

    [Fact]
    public void Undo_collision_never_moves_the_unrelated_conflicting_file_during_rollback()
    {
        Write(Source, "a.txt", "a"); Write(Source, "b.txt", "b");
        var result = WindowsFolderMerge.Run(Source, Target, _operations);
        Write(Source, "b.txt", "unrelated");
        Assert.Throws<IOException>(() => FileUndoApplier.Undo(_operations, result.Undo!));
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(Source, "b.txt")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(Target, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(Target, "b.txt")));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(Target), p => Path.GetFileName(p).StartsWith(".filesmate-undo-", StringComparison.Ordinal));
    }

    [Fact]
    public void Merge_rejects_same_folder_descendants_and_file_targets()
    {
        Assert.Throws<IOException>(() => WindowsFolderMerge.Run(Source, Source, _operations));
        var child = Directory.CreateDirectory(Path.Combine(Source, "child")).FullName;
        Assert.Throws<IOException>(() => WindowsFolderMerge.Run(Source, child, _operations));
        var file = Path.Combine(_root, "file.txt"); File.WriteAllText(file, "keep");
        Assert.Throws<IOException>(() => WindowsFolderMerge.Run(Source, file, _operations));
        Assert.Equal("keep", File.ReadAllText(file));
    }

    [Fact]
    public void Merge_does_not_follow_directory_links_or_touch_their_contents()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        Write(outside, "keep.txt", "keep");
        var link = Path.Combine(Source, "linked");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            var result = WindowsFolderMerge.Run(Source, Target, _operations);
            Assert.Single(result.Errors);
            Assert.Null(result.Undo);
            Assert.False(result.SourceRemoved);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.txt")));
            Assert.Throws<IOException>(() => WindowsFolderMerge.Run(link, Path.Combine(Source, "peer"), _operations));
        }
        finally { Directory.Delete(link); }
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
