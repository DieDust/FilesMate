using FilesMate.App.Services;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Tests.Services;

public sealed class ShelfTransferCompletionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.ShelfCompletion", Guid.NewGuid().ToString("N"));
    private readonly WindowsLocalFileOperations _operations = new();
    private readonly FileShelfStore _shelf;
    public ShelfTransferCompletionTests()
    {
        Directory.CreateDirectory(_root);
        _shelf = new(Path.Combine(_root, "shelf.json"));
    }

    [Fact]
    public async Task Moving_without_undo_removes_the_completed_source_from_the_shelf()
    {
        var source = Write("source/item.txt", "incoming"); var destination = Folder("target");
        Write("target/item.txt", "old"); await _shelf.AddAsync([source]);
        var result = await FileShelfTransfer.RunAsync(_operations, [source], destination, true,
            resolveConflict: (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.ReplaceWithoutUndo)));
        Assert.Empty(result.Errors); Assert.Equal(1, result.WithoutUndo); Assert.Null(result.Undo);
        Assert.Single(result.Completed); Assert.False(File.Exists(source));
        await FileShelfTransfer.RemoveMovedSourcesAsync(_shelf, result);
        Assert.Empty(await _shelf.GetAsync());
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(destination, "item.txt")));
    }

    [Fact]
    public async Task Partial_merge_keeps_skipped_and_externally_missing_sources()
    {
        var moved = Write("source/folder/moved.txt", "move"); var skipped = Write("source/folder/skipped.txt", "skip");
        var source = Path.GetDirectoryName(moved)!; var missing = Path.Combine(_root, "externally-missing.txt");
        var destination = Folder("target"); Write("target/folder/skipped.txt", "keep");
        await _shelf.AddAsync([source, moved, skipped, missing]);
        var result = await FileShelfTransfer.RunAsync(_operations, [source, missing], destination, true,
            resolveConflict: (conflict, _) => Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Skip)));
        Assert.Single(result.Completed); Assert.Single(result.Errors); Assert.Equal(1, result.Skipped);
        Assert.Empty(result.RemovedSourceDirectories);
        await FileShelfTransfer.RemoveMovedSourcesAsync(_shelf, result);
        Assert.Equal(new[] { source, skipped, missing }, await _shelf.GetAsync());
        Assert.Equal("skip", File.ReadAllText(skipped)); Assert.Equal("keep", File.ReadAllText(Path.Combine(destination, "folder/skipped.txt")));
    }

    [Fact]
    public async Task Merging_an_empty_folder_carries_completion_evidence_without_a_file_pair()
    {
        var source = Folder("source/empty"); var destination = Folder("target"); Folder("target/empty");
        await _shelf.AddAsync([source]);
        var result = await FileShelfTransfer.RunAsync(_operations, [source], destination, true,
            resolveConflict: (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Merge)));
        Assert.Empty(result.Completed); Assert.Equal(source, Assert.Single(result.RemovedSourceDirectories));
        Assert.False(Directory.Exists(source));
        await FileShelfTransfer.RemoveMovedSourcesAsync(_shelf, result);
        Assert.Empty(await _shelf.GetAsync()); Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
    }

    [Fact]
    public async Task Cancellation_cleans_completed_paths_and_keeps_an_unprocessed_path_deleted_externally()
    {
        var first = Write("source/first.txt", "first"); var second = Write("source/second.txt", "second");
        await _shelf.AddAsync([first, second]); using var cancel = new CancellationTokenSource();
        var result = await FileShelfTransfer.RunAsync(_operations, [first, second], Folder("target"), true,
            new ProgressNow(_ => { File.Delete(second); cancel.Cancel(); }), cancel.Token);
        Assert.True(result.Cancelled); Assert.Single(result.Completed);
        await FileShelfTransfer.RemoveMovedSourcesAsync(_shelf, result);
        Assert.Equal(second, Assert.Single(await _shelf.GetAsync()));
    }

    [Fact]
    public async Task Moving_a_parent_cleans_nested_shelf_entries_but_keeps_a_recreated_source()
    {
        var child = Write("source/folder/child.txt", "child"); var source = Path.GetDirectoryName(child)!;
        await _shelf.AddAsync([source, child]);
        var result = await FileShelfTransfer.RunAsync(_operations, [source], Folder("target"), true);
        Assert.Single(result.Completed);
        Directory.CreateDirectory(source); // A different item now occupies the old folder name.
        await FileShelfTransfer.RemoveMovedSourcesAsync(_shelf, result);
        Assert.Equal(source, Assert.Single(await _shelf.GetAsync()));
    }

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    private string Write(string name, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_root, name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path;
    }
    private sealed class ProgressNow(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.ShelfCompletion")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(_root, true);
    }
}
