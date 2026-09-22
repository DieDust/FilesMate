using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class RecycleIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.RecycleIdentity", Guid.NewGuid().ToString("N"));
    private readonly WindowsLocalFileOperations _operations = new();
    private readonly List<RecycleItemResult> _receipts = [];

    [Fact]
    public void Undo_restores_the_exact_version_even_after_another_same_path_deletion()
    {
        var path = CreateFile("same.txt", "original");
        var original = Recycle(path);
        var stack = new FileUndoStack(); stack.Push(FileUndoRecord.RecycledWithReceipts([original]));
        File.WriteAllText(path, "later version"); var later = Recycle(path);
        Assert.True(stack.TryUndo(_operations));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal("later version", File.ReadAllText(later.Receipt!.DataPath));
        Assert.True(stack.TryRedo(_operations));
        Assert.True(stack.TryUndo(_operations));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Equal("later version", File.ReadAllText(later.Receipt.DataPath));
    }

    [Fact]
    public void Missing_exact_entry_does_not_restore_another_same_named_file()
    {
        var path = CreateFile("missing.txt", "original"); var original = Recycle(path);
        File.WriteAllText(path, "unrelated"); var unrelated = Recycle(path);
        // Move only this test's exact receipt out of the bin, simulating an independent restore.
        File.Move(original.Receipt!.DataPath, Path.Combine(_root, "kept-original.txt"));
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(_operations, FileUndoRecord.RecycledWithReceipts([original])));
        Assert.False(File.Exists(path)); Assert.Equal("unrelated", File.ReadAllText(unrelated.Receipt!.DataPath));
    }

    [Fact]
    public void Restoring_a_hidden_file_preserves_its_original_attributes()
    {
        var path = CreateFile("hidden.txt", "keep"); File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.Archive);
        var item = Recycle(path);
        _operations.RestoreRecycledItems([item]);
        Assert.True((File.GetAttributes(path) & FileAttributes.Hidden) != 0);
    }

    [Fact]
    public void A_path_only_record_cannot_guess_a_recycle_entry()
    {
        var path = CreateFile("no-receipt.txt", "keep"); var item = Recycle(path);
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(_operations, FileUndoRecord.Recycled([path])));
        Assert.Equal("keep", File.ReadAllText(item.Receipt!.DataPath));
    }

    private string CreateFile(string name, string contents)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, name); File.WriteAllText(path, contents); return path;
    }
    private RecycleItemResult Recycle(string path)
    {
        var items = new List<RecycleItemResult>(); _operations.Recycle([path], items.Add);
        var item = Assert.Single(items); _receipts.Add(item); Assert.NotNull(item.Receipt); return item;
    }
    public void Dispose()
    {
        // Never enumerate or empty the user's bin. Each removed entry was returned by this test's shell operation.
        foreach (var item in _receipts)
        {
            if (item.Receipt is not { } receipt || !item.OriginalPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(receipt.DataPath)) { File.SetAttributes(receipt.DataPath, FileAttributes.Normal); File.Delete(receipt.DataPath); }
            if (File.Exists(receipt.InfoPath)) File.Delete(receipt.InfoPath);
        }
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.RecycleIdentity")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_root))
        {
            foreach (var path in Directory.EnumerateFiles(_root)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
    }
}
