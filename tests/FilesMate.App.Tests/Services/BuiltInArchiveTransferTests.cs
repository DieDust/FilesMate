using System.IO.Compression;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FilesMate.App.Services;
using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Archives;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.App.Tests.Services;

public sealed class BuiltInArchiveTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate.ArchiveTransfer", Guid.NewGuid().ToString("N"));
    private readonly ReplacementBackupBudget _budget = new(1024 * 1024, 1024 * 1024);
    private readonly List<FileUndoRecord> _history = [];
    private readonly TestOperations _operations;

    public BuiltInArchiveTransferTests()
    {
        Directory.CreateDirectory(_root);
        _operations = new TestOperations(Folder("recycled"));
    }

    [Fact]
    public async Task Compress_creates_zip_and_undo_redo_never_recreates_staging()
    {
        var source = Write("source/report.txt", "report"); var target = Folder("target");
        var result = await Run([source], target, compress: true);
        Assert.Empty(result.Errors); Assert.False(result.Cancelled);
        var archive = Path.Combine(target, "report.zip");
        using (var zip = ZipFile.OpenRead(archive)) Assert.Equal("report", Read(zip, "report.txt"));
        var undo = Assert.IsType<FileUndoRecord>(result.Undo);
        Assert.Equal(FileUndoKind.Copied, undo.Kind); Assert.Empty(undo.Pairs);
        Assert.Equal(archive, Assert.Single(undo.Paths));
        FileUndoApplier.Undo(_operations, undo); Assert.False(File.Exists(archive));
        FileUndoApplier.Redo(_operations, undo); Assert.True(File.Exists(archive));
        Assert.Equal("report", File.ReadAllText(source)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Replaced_zip_swaps_versions_repeatedly_with_one_budget_owner()
    {
        var source = Write("source/item.txt", "new"); var target = Folder("target");
        var old = Zip("target/item.zip", ("old.txt", "old")); var original = File.ReadAllBytes(old);
        FileConflict? shown = null;
        var result = await Run([source], target, true, resolve: (conflict, _) =>
        { shown = conflict; return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)); });
        Assert.Empty(result.Errors); Assert.NotNull(shown); Assert.DoesNotContain(".filesmate-archive-", shown.Source);
        var undo = Assert.IsType<FileUndoRecord>(result.Undo);
        Assert.Empty(undo.Paths); Assert.Single(undo.Replacements); Assert.Single(_budget.Entries);
        var bytes = _budget.UsedBytes;
        for (var i = 0; i < 3; i++)
        {
            FileUndoApplier.Undo(_operations, undo); Assert.Equal(original, File.ReadAllBytes(old));
            Assert.Throws<InvalidOperationException>(() => undo.Replacements[0].ConvertToCreatedOperation());
            FileUndoApplier.Redo(_operations, undo);
            using var zip = ZipFile.OpenRead(old); Assert.Equal("new", Read(zip, "item.txt"));
            Assert.Equal(bytes, _budget.UsedBytes); Assert.Single(_budget.Entries); AssertNoStaging(target);
        }
        undo.Replacements[0].Dispose(); undo.Replacements[0].Dispose();
        Assert.Equal(0, _budget.UsedBytes); Assert.Empty(_budget.Entries);
        Assert.True(File.Exists(old)); Assert.Equal("new", File.ReadAllText(source));
    }

    [Fact]
    public async Task Extraction_merge_reuses_skip_and_keep_both_and_preserves_existing_files()
    {
        var archive = Zip("source/data.zip", ("folder/keep.txt", "incoming"), ("folder/new.txt", "new"));
        var target = Folder("target"); Write("target/folder/keep.txt", "existing");
        var result = await Run([archive], target, false, resolve: (conflict, _) => Task.FromResult(
            new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.KeepBoth)));
        Assert.Empty(result.Errors); Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "folder/keep.txt")));
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(target, "folder/keep (2).txt")));
        var undo = Assert.IsType<FileUndoRecord>(result.Undo);
        FileUndoApplier.Undo(_operations, undo);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "folder/keep.txt")));
        Assert.False(File.Exists(Path.Combine(target, "folder/new.txt"))); Assert.True(File.Exists(archive));
        FileUndoApplier.Redo(_operations, undo); Assert.Equal("new", File.ReadAllText(Path.Combine(target, "folder/new.txt")));
        AssertNoStaging(target);
    }

    [Fact]
    public async Task Canceling_later_conflict_keeps_completed_replacement_undo_and_remaining_targets()
    {
        var archive = Zip("source/data.zip", ("a.txt", "new-a"), ("b.txt", "new-b"));
        var target = Folder("target"); Write("target/a.txt", "old-a"); Write("target/b.txt", "old-b");
        var calls = 0;
        var result = await Run([archive], target, false, resolve: (_, _) => Task.FromResult(new FileConflictChoice(
            ++calls == 1 ? FileConflictAction.Replace : FileConflictAction.Cancel)));
        Assert.True(result.Cancelled); Assert.Empty(result.Errors); var completed = Assert.Single(result.Completed);
        Assert.DoesNotContain(".filesmate-archive-", completed.Source);
        var undo = Assert.IsType<FileUndoRecord>(result.Undo); Assert.Single(undo.Replacements);
        FileUndoApplier.Undo(_operations, undo);
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Equal("old-b", File.ReadAllText(Path.Combine(target, "b.txt")));
        FileUndoApplier.Redo(_operations, undo);
        Assert.StartsWith("new-", File.ReadAllText(completed.Destination)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Corrupt_later_archive_publishes_none_and_preserves_inputs_and_existing_target()
    {
        var good = Zip("source/good.zip", ("a.txt", "new")); var bad = Write("source/bad.zip", "broken");
        var target = Folder("target"); Write("target/a.txt", "existing");
        var result = await Run([good, bad], target, false);
        Assert.NotEmpty(result.Errors); Assert.Empty(result.Completed); Assert.Null(result.Undo);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.True(File.Exists(good)); Assert.Equal("broken", File.ReadAllText(bad)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Cancellation_during_preparation_removes_only_own_staging()
    {
        var archive = Zip("source/data.zip", ("a.txt", new string('a', 200_000)));
        var target = Folder("target"); var foreign = Folder("target/.filesmate-archive-foreign");
        Write("target/.filesmate-archive-foreign/keep.txt", "keep"); using var cancel = new CancellationTokenSource();
        var result = await Run([archive], target, false, token: cancel.Token,
            progress: new InlineProgress(_ => cancel.Cancel()));
        Assert.True(result.Cancelled); Assert.Empty(result.Completed); Assert.Null(result.Undo);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(foreign, "keep.txt")));
        Assert.Equal(foreign, Assert.Single(Directory.GetDirectories(target, ".filesmate-archive-*")));
    }

    [Theory]
    [InlineData(true, false, "data/a.txt")]
    [InlineData(false, true, "data/a.txt")]
    [InlineData(false, false, "a.txt")]
    public async Task Extraction_uses_requested_folder_layout(bool createSubfolder, bool smartExtract, string output)
    {
        var archive = Zip("source/data.zip", ("a.txt", "a")); var target = Folder("target");
        var result = await Run([archive], target, false, createSubfolder: createSubfolder, smartExtract: smartExtract);
        Assert.Empty(result.Errors); Assert.Equal("a", File.ReadAllText(Path.Combine(target, output))); AssertNoStaging(target);
    }

    [Fact]
    public async Task Smart_extraction_keeps_a_single_existing_top_level_folder()
    {
        var archive = Zip("source/data.zip", ("package/a.txt", "a")); var target = Folder("target");
        var result = await Run([archive], target, false, smartExtract: true);
        Assert.Empty(result.Errors); Assert.Equal("a", File.ReadAllText(Path.Combine(target, "package/a.txt")));
        Assert.False(Directory.Exists(Path.Combine(target, "data"))); AssertNoStaging(target);
    }

    [Fact]
    public async Task Multiple_archives_merging_into_new_folder_recycle_each_output_only_once()
    {
        var first = Zip("source/first.zip", ("package/a.txt", "a"));
        var second = Zip("source/second.zip", ("package/b.txt", "b")); var target = Folder("target");
        var result = await Run([first, second], target, false, resolve: (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Merge)));
        Assert.Empty(result.Errors); var undo = Assert.IsType<FileUndoRecord>(result.Undo);
        Assert.Equal(Path.Combine(target, "package"), Assert.Single(undo.Paths));
        FileUndoApplier.Undo(_operations, undo); Assert.False(Directory.Exists(Path.Combine(target, "package")));
        FileUndoApplier.Redo(_operations, undo);
        Assert.Equal("a", File.ReadAllText(Path.Combine(target, "package/a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(target, "package/b.txt"))); AssertNoStaging(target);
    }

    [Fact]
    public async Task Self_containing_compression_and_duplicate_root_names_fail_without_writes()
    {
        var first = Write("source/a.txt", "first"); var second = Write("other/a.txt", "second");
        var result = await Run([Path.GetDirectoryName(first)!], Folder("source/inside"), true);
        Assert.NotEmpty(result.Errors); AssertNoStaging(Path.GetDirectoryName(first)!);
        var target = Folder("target"); result = await Run([first, second], target, true);
        Assert.NotEmpty(result.Errors); Assert.Empty(Directory.GetFileSystemEntries(target));
        Assert.Equal("first", File.ReadAllText(first)); Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public async Task Staging_cleanup_unlinks_an_injected_directory_link_without_following_it()
    {
        var archive = Zip("source/data.zip", ("a.txt", "a")); var target = Folder("target");
        var foreign = Folder("foreign"); var sentinel = Write("foreign/keep.txt", "keep"); var inserted = false;
        var result = await Run([archive], target, false, progress: new InlineProgress(_ =>
        {
            if (inserted) return;
            inserted = true;
            var owned = Assert.Single(Directory.GetDirectories(target, ".filesmate-archive-*"));
            Directory.CreateSymbolicLink(Path.Combine(owned, "foreign-link"), foreign);
        }));
        Assert.True(inserted); Assert.NotEmpty(result.Errors); Assert.Empty(result.Completed);
        Assert.Equal("keep", File.ReadAllText(sentinel)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Linked_destination_is_rejected_before_creating_staging()
    {
        var source = Write("source/a.txt", "a"); var foreign = Folder("foreign");
        var alias = Path.Combine(_root, "alias"); Directory.CreateSymbolicLink(alias, foreign);
        var result = await Run([source], alias, true);
        Assert.NotEmpty(result.Errors); Assert.Empty(Directory.GetFileSystemEntries(foreign));
        Directory.Delete(alias, recursive: false);
    }

    [Fact]
    public async Task Failure_after_one_publication_preserves_completed_undo_and_reports_error()
    {
        var archive = Zip("source/data.zip", ("a.txt", "a"), ("b.txt", "b")); var target = Folder("target");
        _operations.FailRename = (_, destination) => Path.GetFileName(destination) == "b.txt" ? new IOException("controlled write failure") : null;
        var result = await Run([archive], target, false);
        Assert.Single(result.Completed); Assert.Single(result.Errors); Assert.False(result.Cancelled);
        Assert.False(File.Exists(Path.Combine(target, "b.txt")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(target, "a.txt"))); Assert.True(File.Exists(archive));
        _operations.FailRename = null;
        var undo = Assert.IsType<FileUndoRecord>(result.Undo);
        FileUndoApplier.Undo(_operations, undo); Assert.False(File.Exists(Path.Combine(target, "a.txt")));
        FileUndoApplier.Redo(_operations, undo); Assert.Equal("a", File.ReadAllText(Path.Combine(target, "a.txt")));
        AssertNoStaging(target);
    }

    [Fact]
    public async Task Explicit_replacement_without_backup_respects_budget_and_returns_no_undo()
    {
        var archive = Zip("source/data.zip", ("a.txt", "new")); var target = Folder("target"); Write("target/a.txt", "old");
        var budget = new ReplacementBackupBudget(0, 0); var asked = false;
        var result = await BuiltInArchiveTransfer.RunAsync(_operations, [archive], target, compress: false,
            resolveConflict: (conflict, _) =>
            {
                asked = true; Assert.True(conflict.BackupUnavailable);
                return Task.FromResult(new FileConflictChoice(FileConflictAction.ReplaceWithoutUndo));
            }, backupBudget: budget);
        Assert.True(asked); Assert.Empty(result.Errors); Assert.Single(result.Completed); Assert.Null(result.Undo);
        Assert.Equal(1, result.WithoutUndo); Assert.Equal(0, budget.UsedBytes); Assert.Empty(budget.Entries);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "a.txt"))); Assert.True(File.Exists(archive)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Skipping_all_conflicts_keeps_old_files_and_cleans_prepared_output()
    {
        var archive = Zip("source/data.zip", ("a.txt", "new")); var target = Folder("target"); Write("target/a.txt", "old");
        var result = await Run([archive], target, false, resolve: (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Skip)));
        Assert.Empty(result.Errors); Assert.Empty(result.Completed); Assert.Null(result.Undo); Assert.Equal(1, result.Skipped);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "a.txt"))); AssertNoStaging(target);
    }

    [Fact]
    public async Task Empty_folder_output_is_undoable_and_restorable()
    {
        var archive = Zip("source/data.zip", ("empty/", "")); var target = Folder("target");
        var result = await Run([archive], target, false); Assert.Empty(result.Errors);
        var undo = Assert.IsType<FileUndoRecord>(result.Undo); Assert.True(Directory.Exists(Path.Combine(target, "empty")));
        FileUndoApplier.Undo(_operations, undo); Assert.False(Directory.Exists(Path.Combine(target, "empty")));
        FileUndoApplier.Redo(_operations, undo); Assert.True(Directory.Exists(Path.Combine(target, "empty"))); AssertNoStaging(target);
    }

    [Fact]
    public async Task Preparation_pins_destination_and_owned_root_against_rename()
    {
        var archive = Zip("source/data.zip", ("a.txt", "a")); var target = Folder("target"); var checkedOnce = false;
        var result = await Run([archive], target, false, progress: new InlineProgress(_ =>
        {
            if (checkedOnce) return;
            checkedOnce = true;
            var owned = Assert.Single(Directory.GetDirectories(target, ".filesmate-archive-*"));
            Assert.Throws<IOException>(() => Directory.Move(owned, owned + "-external"));
            Assert.Throws<IOException>(() => Directory.Move(target, target + "-external"));
        }));
        Assert.True(checkedOnce); Assert.Empty(result.Errors); Assert.Equal("a", File.ReadAllText(Path.Combine(target, "a.txt")));
        AssertNoStaging(target);
    }

    [Fact]
    public async Task Publication_pins_roots_and_keeps_owned_staging_nonempty_until_cleanup()
    {
        var source = Write("source/item.txt", "data"); var target = Folder("target");
        var foreign = Folder("foreign"); var sentinel = Write("foreign/keep.txt", "keep"); var inspected = false;
        _operations.AfterRename = (_, destination) =>
        {
            if (destination != Path.Combine(target, "item.zip")) return;
            inspected = true;
            var owned = Assert.Single(Directory.GetDirectories(target, ".filesmate-archive-*"));
            Assert.StartsWith(".owner-", Path.GetFileName(Assert.Single(Directory.GetFileSystemEntries(owned))));
            Assert.Throws<IOException>(() => Directory.Move(owned, owned + "-external"));
            Assert.Throws<IOException>(() => Directory.Move(target, target + "-external"));
            using var writeAttributes = CreateFileW(owned, 0x100, FileShare.ReadWrite | FileShare.Delete, 0, 3, 0x02200000, 0);
            Assert.False(writeAttributes.IsInvalid);
            var mountPoint = MountPointData(foreign);
            Assert.False(DeviceIoControl(writeAttributes, 0x000900A4, mountPoint, (uint)mountPoint.Length, 0, 0, out var returned, 0));
            Assert.Equal((FileAttributes)0, File.GetAttributes(owned) & FileAttributes.ReparsePoint);
        };
        var result = await Run([source], target, true);
        Assert.True(inspected); Assert.Empty(result.Errors); Assert.Single(result.Completed); Assert.NotNull(result.Undo);
        Assert.Equal("keep", File.ReadAllText(sentinel)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Production_operations_publish_zip_and_capture_created_history()
    {
        var source = Write("source/item.txt", "data"); var target = Folder("target");
        var result = await BuiltInArchiveTransfer.RunAsync(new WindowsLocalFileOperations(), [source], target,
            compress: true, backupBudget: _budget);
        Assert.Empty(result.Errors); Assert.Single(result.Completed); Assert.False(result.Cancelled);
        var undo = Assert.IsType<FileUndoRecord>(result.Undo); Assert.Equal(FileUndoKind.Copied, undo.Kind);
        Assert.Equal(Path.Combine(target, "item.zip"), Assert.Single(undo.Paths)); Assert.Empty(undo.Pairs);
        using var zip = ZipFile.OpenRead(undo.Paths[0]); Assert.Equal("data", Read(zip, "item.txt"));
        AssertNoStaging(target);
    }

    [Fact]
    public async Task Publication_errors_preserve_recovery_locations_while_mapping_private_sources()
    {
        var archive = Zip("source/data.zip", ("a.txt", "new")); var target = Folder("target");
        var backup = Write("target/.filesmate-history-controlled/a.txt", "recoverable");
        _operations.FailRename = (_, destination) => new IOException(
            $"The new version was written to: {destination}. Previous version retained at: {backup}.");
        var result = await Run([archive], target, false);
        var error = Assert.Single(result.Errors);
        Assert.Contains(Path.Combine(target, "a.txt"), error); Assert.Contains(backup, error);
        Assert.Contains(Path.Combine(Path.GetFullPath(archive), "a.txt"), error); Assert.DoesNotContain(".filesmate-archive-", error);
        Assert.Equal("recoverable", File.ReadAllText(backup)); AssertNoStaging(target);
    }

    [Fact]
    public async Task Multiple_archives_replacing_inside_a_new_folder_keep_repeatable_history()
    {
        var first = Zip("source/first.zip", ("package/a.txt", "first"));
        var second = Zip("source/second.zip", ("package/a.txt", "second"), ("package/b.txt", "b")); var target = Folder("target");
        var result = await Run([first, second], target, false, resolve: (conflict, _) => Task.FromResult(
            new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Replace)));
        Assert.Empty(result.Errors); var undo = Assert.IsType<FileUndoRecord>(result.Undo);
        for (var i = 0; i < 3; i++)
        {
            FileUndoApplier.Undo(_operations, undo); Assert.False(File.Exists(Path.Combine(target, "package/a.txt")));
            FileUndoApplier.Redo(_operations, undo); Assert.Equal("second", File.ReadAllText(Path.Combine(target, "package/a.txt")));
            Assert.Equal("b", File.ReadAllText(Path.Combine(target, "package/b.txt"))); AssertNoStaging(target);
        }
    }

    private async Task<FileTransferResult> Run(IReadOnlyList<string> sources, string target, bool compress,
        FileConflictResolver? resolve = null, bool createSubfolder = false, bool smartExtract = false,
        CancellationToken token = default, IProgress<ArchiveProgress>? progress = null)
    {
        var result = await BuiltInArchiveTransfer.RunAsync(_operations, sources, target, compress,
            createSubfolder: createSubfolder, resolveConflict: resolve, progress: progress, token: token,
            backupBudget: _budget, smartExtract: smartExtract);
        if (result.Undo is { } undo) _history.Add(undo);
        return result;
    }
    private string Folder(string path) => Directory.CreateDirectory(Path.Combine(_root, path)).FullName;
    private string Write(string path, string text)
    {
        var full = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text); return full;
    }
    private string Zip(string path, params (string Name, string Text)[] entries)
    {
        var full = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var zip = ZipFile.Open(full, ZipArchiveMode.Create);
        foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Text); }
        return full;
    }
    private static string Read(ZipArchive zip, string entry) { using var reader = new StreamReader(zip.GetEntry(entry)!.Open()); return reader.ReadToEnd(); }
    private static byte[] MountPointData(string target)
    {
        var path = Path.GetFullPath(target);
        var substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + path);
        var print = System.Text.Encoding.Unicode.GetBytes(path);
        var bytes = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xA0000003);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)(bytes.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), (ushort)print.Length);
        substitute.CopyTo(bytes, 16); print.CopyTo(bytes, 16 + substitute.Length + 2);
        return bytes;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access,
        FileShare share, nint security, uint mode, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code,
        byte[] input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);
    private static void AssertNoStaging(string target) => Assert.Empty(Directory.GetDirectories(target, ".filesmate-archive-*", SearchOption.AllDirectories));
    private sealed class InlineProgress(Action<ArchiveProgress> report) : IProgress<ArchiveProgress> { public void Report(ArchiveProgress value) => report(value); }
    public void Dispose()
    {
        foreach (var replacement in _history.SelectMany(record => record.Replacements).Distinct()) replacement.Dispose();
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMate.ArchiveTransfer")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(_root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(_root, true);
    }

    // Undo uses fixture-owned quarantine so these tests never modify the user's Recycle Bin.
    private sealed class TestOperations(string quarantine) : ILocalFileOperations
    {
        private readonly WindowsLocalFileOperations _native = new();
        private readonly Dictionary<string, string> _recycled = new(StringComparer.OrdinalIgnoreCase);
        internal Func<string, string, Exception?>? FailRename { get; set; }
        internal Action<string, string>? AfterRename { get; set; }
        public bool RequiresUndoValidation => true;
        public void CreateDirectory(string path, bool failIfExists = false) => _native.CreateDirectory(path, failIfExists);
        public void CreateEmptyFile(string path) => _native.CreateEmptyFile(path);
        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory) => _native.Copy(sources, destinationDirectory);
        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory) => _native.Move(sources, destinationDirectory);
        public void Rename(string source, string destinationPath)
        {
            if (FailRename?.Invoke(source, destinationPath) is { } error) throw error;
            _native.Rename(source, destinationPath);
            AfterRename?.Invoke(source, destinationPath);
        }
        public bool TryRenameFileWithoutCopy(string source, string destinationPath)
        {
            if (FailRename?.Invoke(source, destinationPath) is { } error) throw error;
            var moved = _native.TryRenameFileWithoutCopy(source, destinationPath);
            if (moved) AfterRename?.Invoke(source, destinationPath);
            return moved;
        }
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        {
            foreach (var path in paths)
            {
                var stored = Path.Combine(quarantine, Guid.NewGuid().ToString("N")); Rename(path, stored);
                _recycled.Add(path, stored); completed?.Invoke(new(path, true));
            }
        }
        public void RestoreRecycled(IReadOnlyList<string> originalPaths) => throw new NotSupportedException();
        public void RestoreRecycledItems(IReadOnlyList<RecycleItemResult> items, Action<string>? completed = null)
        {
            foreach (var item in items) { Rename(_recycled[item.OriginalPath], item.OriginalPath); _recycled.Remove(item.OriginalPath); completed?.Invoke(item.OriginalPath); }
        }
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
