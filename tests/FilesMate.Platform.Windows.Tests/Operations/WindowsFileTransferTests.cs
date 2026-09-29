using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class WindowsFileTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMate-conflict-" + Guid.NewGuid().ToString("N"));
    private readonly WindowsLocalFileOperations _operations = new();
    private string Source => Path.Combine(_root, "source");
    private string Target => Path.Combine(_root, "target");
    public WindowsFileTransferTests() { Directory.CreateDirectory(Source); Directory.CreateDirectory(Target); }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Copy_policy_keeps_both_without_a_prompt_even_with_no_backup_budget(bool sameFolder)
    {
        var source = Write(Source, "image.png", "incoming");
        var destination = sameFolder ? source : Write(Target, "image.png", "existing");
        var budget = new ReplacementBackupBudget(0, 0);
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, destination)], false,
            (_, _) => throw new InvalidOperationException("Copy must not ask"), backupBudget: budget,
            copyCollisionPolicy: FileCopyCollisionPolicy.KeepBoth);
        Assert.Empty(result.Errors); Assert.False(result.Cancelled);
        var pair = Assert.Single(result.Completed);
        Assert.Equal("image (2).png", Path.GetFileName(pair.Destination));
        Assert.Equal("incoming", File.ReadAllText(source));
        Assert.Equal(sameFolder ? "incoming" : "existing", File.ReadAllText(destination));
        Assert.Equal("incoming", File.ReadAllText(pair.Destination));
        Assert.NotNull(result.Undo); Assert.Empty(result.Undo.Replacements); Assert.Equal(0, budget.UsedBytes);
    }

    [Fact]
    public async Task Repeated_same_names_in_one_copy_batch_allocate_distinct_files()
    {
        var first = Write(Source, "first/a.txt", "one");
        var second = Write(Source, "second/a.txt", "two");
        var third = Write(Source, "third/a.txt", "three");
        var target = Path.Combine(Target, "a.txt");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(first, target), new(second, target), new(third, target)], false,
            copyCollisionPolicy: FileCopyCollisionPolicy.KeepBoth);
        Assert.Empty(result.Errors); Assert.Equal(3, result.Completed.Count);
        Assert.Equal(new[] { "one", "two", "three" }, result.Completed.Select(p => File.ReadAllText(p.Destination)));
        Assert.Equal(new[] { "a.txt", "a (2).txt", "a (3).txt" }, result.Completed.Select(p => Path.GetFileName(p.Destination)));
    }

    [Fact]
    public async Task Copying_a_folder_duplicates_the_whole_folder_instead_of_merging_it()
    {
        Write(Source, "report.v1/nested/item.txt", "incoming"); Write(Target, "report.v1/keep.txt", "existing");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(Path.Combine(Source, "report.v1"), Path.Combine(Target, "report.v1"))], false,
            copyCollisionPolicy: FileCopyCollisionPolicy.KeepBoth);
        Assert.Empty(result.Errors); Assert.False(result.Cancelled);
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(Target, "report.v1 (2)/nested/item.txt")));
        Assert.False(File.Exists(Path.Combine(Target, "report.v1/nested/item.txt")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(Target, "report.v1/keep.txt")));
    }

    [Fact]
    public async Task Copy_over_a_directory_with_the_same_name_allocates_a_file_sibling()
    {
        var source = Write(Source, "name", "file"); var target = Path.Combine(Target, "name");
        Directory.CreateDirectory(target);
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false,
            copyCollisionPolicy: FileCopyCollisionPolicy.KeepBoth);
        Assert.Empty(result.Errors); Assert.True(Directory.Exists(target));
        Assert.Equal("file", File.ReadAllText(Assert.Single(result.Completed).Destination));
        Assert.Equal("name (2)", Path.GetFileName(result.Completed[0].Destination));
    }

    [Fact]
    public async Task Copy_retries_if_another_writer_takes_the_numbered_name_before_publication()
    {
        var source = Write(Source, "item.txt", new string('a', 64 * 1024));
        var target = Write(Target, "item.txt", "existing"); var raced = Path.Combine(Target, "item (2).txt");
        var claimed = false;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false,
            byteProgress: new InlineProgress(_ => { if (!claimed) { claimed = true; File.WriteAllText(raced, "other writer"); } }),
            copyCollisionPolicy: FileCopyCollisionPolicy.KeepBoth);
        Assert.True(claimed); Assert.Empty(result.Errors);
        Assert.Equal("item (3).txt", Path.GetFileName(Assert.Single(result.Completed).Destination));
        Assert.Equal("other writer", File.ReadAllText(raced)); Assert.Equal("existing", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(Target, ".filesmate-copy-*"));
    }

    [Fact]
    public async Task Copy_policy_never_suppresses_move_conflicts_and_batch_skip_leaves_sources()
    {
        var first = Write(Source, "a", "incoming a"); var second = Write(Source, "b", "incoming b");
        var a = Write(Target, "a", "old a"); var b = Write(Target, "b", "old b"); var calls = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(first, a), new(second, b)], true,
            (conflict, _) =>
            {
                calls++; Assert.True(conflict.IsMove); Assert.True(conflict.IsBatch);
                Assert.Equal(conflict.Source, conflict.SourcePreviewPath);
                return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip, true));
            }, copyCollisionPolicy: FileCopyCollisionPolicy.KeepBoth);
        Assert.Equal(1, calls); Assert.Equal(2, result.Skipped); Assert.Empty(result.Completed);
        Assert.True(File.Exists(first)); Assert.True(File.Exists(second));
        Assert.Equal("old a", File.ReadAllText(a)); Assert.Equal("old b", File.ReadAllText(b));
    }

    [Fact]
    public async Task Files_changed_during_comparison_are_flagged_for_a_new_decision()
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "existing"); var calls = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], true, (conflict, _) =>
        {
            if (++calls == 1)
            {
                Assert.False(conflict.ChangedSinceDecision); File.WriteAllText(target, "a new unapproved version");
                return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace, true));
            }
            Assert.True(conflict.ChangedSinceDecision);
            return Task.FromResult(new FileConflictChoice(FileConflictAction.Cancel));
        });
        Assert.Equal(2, calls); Assert.True(result.Cancelled); Assert.Empty(result.Completed);
        Assert.Equal("a new unapproved version", File.ReadAllText(target)); Assert.Equal("incoming", File.ReadAllText(source));
    }

    [Fact]
    public async Task Cancellation_during_one_file_keeps_source_and_never_publishes_partial_destination()
    {
        var source = Path.Combine(Source, "large.bin");
        using (var file = File.Create(source)) file.SetLength(32L * 1024 * 1024);
        var target = Path.Combine(Target, "large.bin");
        using var cancel = new CancellationTokenSource();
        long observed = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false, token: cancel.Token,
            byteProgress: new InlineProgress(value => { observed = value.Transferred; cancel.Cancel(); }));
        Assert.True(result.Cancelled);
        Assert.Empty(result.Completed);
        Assert.Empty(result.Errors);
        Assert.InRange(observed, 1, new FileInfo(source).Length - 1);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Target));
        Assert.Equal(32L * 1024 * 1024, new FileInfo(source).Length);
    }

    private sealed class InlineProgress(Action<FileCopyProgress> report) : IProgress<FileCopyProgress>
    {
        public void Report(FileCopyProgress value) => report(value);
    }

    [Fact]
    public async Task BudgetExhaustionInterruptsRememberedReplaceAndNeverExceedsBatchLimit()
    {
        var budget = new ReplacementBackupBudget(16, 16);
        var requests = Enumerable.Range(0, 3).Select(i => new FilePathPair(Write(Source, i.ToString(), "incoming"), Write(Target, i.ToString(), "original"))).ToArray();
        var prompts = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, requests, false, (conflict, _) =>
        {
            prompts++;
            return Task.FromResult(new FileConflictChoice(conflict.BackupUnavailable ? FileConflictAction.Skip : FileConflictAction.Replace, true));
        }, backupBudget: budget);
        Assert.Empty(result.Errors); Assert.Equal(2, prompts); Assert.Equal(2, result.Completed.Count); Assert.Equal(1, result.Skipped);
        Assert.Equal(16, budget.UsedBytes); Assert.Equal(2, Directory.GetDirectories(Target, ".filesmate-history-*").Length);
        Assert.Equal("original", File.ReadAllText(requests[2].Destination));
        foreach (var item in result.Undo!.Replacements) item.Dispose();
        Assert.Equal(0, budget.UsedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitUnprotectedReplacementLeavesNoBackupAndCannotBeRemembered(bool move)
    {
        var budget = new ReplacementBackupBudget(4, 4);
        var requests = Enumerable.Range(0, 2).Select(i => new FilePathPair(Write(Source, i.ToString(), "incoming"), Write(Target, i.ToString(), "original"))).ToArray();
        var prompts = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, requests, move, (conflict, _) =>
        {
            prompts++; Assert.True(conflict.BackupUnavailable);
            return Task.FromResult(new FileConflictChoice(FileConflictAction.ReplaceWithoutUndo, true));
        }, backupBudget: budget);
        Assert.Empty(result.Errors); Assert.Equal(2, result.WithoutUndo); Assert.Equal(2, prompts); Assert.Null(result.Undo);
        Assert.Empty(Directory.GetDirectories(Target, ".filesmate-history-*")); Assert.Equal(0, budget.UsedBytes);
        foreach (var pair in requests) { Assert.Equal("incoming", File.ReadAllText(pair.Destination)); Assert.Equal(!move, File.Exists(pair.Source)); }
    }

    [Fact]
    public async Task UnprotectedChoiceInvalidatesWholeBatchUndoWithoutLeavingEarlierBackups()
    {
        var budget = new ReplacementBackupBudget(8, 8);
        var requests = Enumerable.Range(0, 2).Select(i => new FilePathPair(Write(Source, i.ToString(), "incoming"), Write(Target, i.ToString(), "original"))).ToArray();
        var result = await WindowsFileTransfer.RunAsync(_operations, requests, false, (conflict, _) => Task.FromResult(
            new FileConflictChoice(conflict.BackupUnavailable ? FileConflictAction.ReplaceWithoutUndo : FileConflictAction.Replace)), backupBudget: budget);
        Assert.Empty(result.Errors); Assert.Null(result.Undo); Assert.Equal(1, result.WithoutUndo);
        Assert.Empty(Directory.GetDirectories(Target, ".filesmate-history-*")); Assert.Equal(0, budget.UsedBytes);
    }

    [Fact]
    public async Task BudgetReservesBothRedoSizeAndAlternateStreamsBeforeWriting()
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "old");
        File.WriteAllText(source + ":stream", "metadata");
        var budget = new ReplacementBackupBudget(100, 10);
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false, (conflict, _) =>
        {
            Assert.Equal(16, conflict.BackupBytes); Assert.True(conflict.BackupUnavailable);
            return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)); // Invalid client cannot bypass the engine's cap.
        }, backupBudget: budget);
        Assert.Single(result.Errors); Assert.Empty(result.Completed); Assert.Equal("old", File.ReadAllText(target));
        Assert.Empty(Directory.GetDirectories(Target, ".filesmate-history-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_retains_both_versions_for_undo_redo_and_releases_the_backup(bool move)
    {
        var source = Write(Source, "replace.txt", "incoming");
        var target = Write(Target, "replace.txt", "original");
        File.WriteAllText(source + ":test-stream", "incoming stream");
        File.WriteAllText(target + ":test-stream", "original stream");
        var operations = new TestOperations(_root);
        var result = await WindowsFileTransfer.RunAsync(operations, [new(source, target)], move, (conflict, _) =>
        {
            Assert.True(conflict.CanReplace); Assert.NotNull(conflict.Incoming); Assert.NotNull(conflict.Existing);
            return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace));
        });
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors)); Assert.Single(result.Completed);
        Assert.Equal("incoming", File.ReadAllText(target)); Assert.Equal(!move, File.Exists(source));
        var stack = new FileUndoStack(); stack.Push(result.Undo!);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(stack.TryUndo(operations)); Assert.Equal("original", File.ReadAllText(target));
            Assert.Equal("original stream", File.ReadAllText(target + ":test-stream"));
            Assert.Equal("incoming", File.ReadAllText(source));
            Assert.True(stack.TryRedo(operations)); Assert.Equal("incoming", File.ReadAllText(target));
            Assert.Equal("incoming stream", File.ReadAllText(target + ":test-stream"));
        }
        var saved = Assert.Single(result.Undo!.Replacements).Backup;
        Assert.True(File.Exists(saved)); stack.Clear(); Assert.False(File.Exists(saved));
        Assert.Equal("incoming", File.ReadAllText(target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_merged_folder_and_ordinary_files_can_be_undone_together(bool move)
    {
        Write(Source, "nested/same", "incoming"); Write(Target, "nested/same", "original");
        Write(Source, "other", "another");
        var ops = new TestOperations(_root);
        var result = await WindowsFileTransfer.RunAsync(ops, [new(Source, Target)], move,
            (conflict, _) => Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Replace, true)));
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors)); Assert.Equal(2, result.Completed.Count);
        FileUndoApplier.Undo(ops, result.Undo!);
        Assert.Equal("original", File.ReadAllText(Path.Combine(Target, "nested/same")));
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(Source, "nested/same")));
        Assert.False(File.Exists(Path.Combine(Target, "other")));
        FileUndoApplier.Redo(ops, result.Undo!);
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(Target, "nested/same")));
        Assert.Equal("another", File.ReadAllText(Path.Combine(Target, "other")));
        Assert.Equal(!move, Directory.Exists(Source));
        foreach (var replacement in result.Undo!.Replacements) replacement.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_replace_preserves_both_original_paths(bool move)
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "original");
        using var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], move,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
        Assert.Single(result.Errors); Assert.Empty(result.Completed); Assert.Null(result.Undo);
        Assert.Equal("incoming", File.ReadAllText(source)); Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(Target));
    }

    [Fact]
    public async Task Changed_comparison_is_prompted_again_before_replacement()
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "original");
        var count = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false, (_, _) =>
        {
            count++;
            if (count == 1) { File.WriteAllText(target, "a later change"); return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace, true)); }
            return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip));
        });
        Assert.Equal(2, count); Assert.Empty(result.Errors); Assert.Equal("a later change", File.ReadAllText(target));
    }

    [Fact]
    public async Task Replace_all_never_applies_to_different_types_or_self_copy()
    {
        var a = Write(Source, "a", "incoming"); var b = Write(Target, "a", "original");
        var c = Write(Source, "folder", "file"); Directory.CreateDirectory(Path.Combine(Target, "folder"));
        var calls = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(a, b), new(c, Path.Combine(Target, "folder")), new(a, a)], false, (conflict, _) =>
        {
            calls++;
            return Task.FromResult(new FileConflictChoice(conflict.CanReplace ? FileConflictAction.Replace : FileConflictAction.Skip, conflict.CanReplace));
        });
        Assert.Empty(result.Errors); Assert.Equal(3, calls); Assert.Equal(2, result.Skipped);
        Assert.Equal("incoming", File.ReadAllText(a)); Assert.True(Directory.Exists(Path.Combine(Target, "folder")));
        Assert.Single(result.Undo!.Replacements).Dispose();
    }

    [Fact]
    public async Task Undo_refuses_to_overwrite_a_file_edited_after_replacement()
    {
        var a = Write(Source, "a", "incoming"); var b = Write(Target, "a", "original");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(a, b)], false,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
        File.WriteAllText(b, "subsequent edit");
        Assert.Throws<UndoStateChangedException>(() => FileUndoApplier.Undo(_operations, result.Undo!));
        Assert.Equal("subsequent edit", File.ReadAllText(b));
        Assert.Single(result.Undo!.Replacements).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_named_sources_in_one_batch_undo_and_redo_in_order(bool move)
    {
        var first = Write(Source, "one/a", "first"); var second = Write(Source, "two/a", "second");
        var target = Path.Combine(Target, "a"); var ops = new TestOperations(_root);
        var result = await WindowsFileTransfer.RunAsync(ops, [new(first, target), new(second, target)], move,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
        Assert.Empty(result.Errors); Assert.Equal("second", File.ReadAllText(target));
        FileUndoApplier.Undo(ops, result.Undo!);
        Assert.False(File.Exists(target)); Assert.Equal("first", File.ReadAllText(first)); Assert.Equal("second", File.ReadAllText(second));
        FileUndoApplier.Redo(ops, result.Undo!); Assert.Equal("second", File.ReadAllText(target));
        Assert.Single(result.Undo!.Replacements).Dispose();
    }

    [Fact]
    public async Task Replace_all_files_is_scoped_and_cancellation_retains_completed_undo()
    {
        var a = Write(Source, "a", "new a"); var b = Write(Source, "b", "new b");
        var ta = Write(Target, "a", "old a"); var tb = Write(Target, "b", "old b");
        var count = 0; var ops = new TestOperations(_root);
        var result = await WindowsFileTransfer.RunAsync(ops, [new(a, ta), new(b, tb)], false, (_, _) =>
        { count++; return Task.FromResult(new FileConflictChoice(FileConflictAction.Replace, true)); });
        Assert.Equal(1, count); Assert.Equal(2, result.Completed.Count); Assert.Empty(result.Errors);
        FileUndoApplier.Undo(ops, result.Undo!);
        foreach (var r in result.Undo!.Replacements) r.Dispose();
        count = 0;
        result = await WindowsFileTransfer.RunAsync(ops, [new(a, ta), new(b, tb)], false, (_, _) =>
        { count++; return Task.FromResult(new FileConflictChoice(count == 1 ? FileConflictAction.Replace : FileConflictAction.Cancel)); });
        Assert.True(result.Cancelled); Assert.Single(result.Completed);
        FileUndoApplier.Undo(ops, result.Undo!);
        Assert.Equal("old a", File.ReadAllText(ta)); Assert.Equal("old b", File.ReadAllText(tb));
        Assert.Single(result.Undo!.Replacements).Dispose();
    }

    [Fact]
    public async Task Undo_move_preserves_a_new_file_at_the_original_source_path()
    {
        var a = Write(Source, "a", "incoming"); var b = Write(Target, "a", "original");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(a, b)], true,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
        Assert.Empty(result.Errors); File.WriteAllText(a, "later source");
        Assert.Throws<IOException>(() => FileUndoApplier.Undo(_operations, result.Undo!));
        Assert.Equal("later source", File.ReadAllText(a)); Assert.Equal("incoming", File.ReadAllText(b));
        Assert.Single(result.Undo!.Replacements).Dispose();
    }

    [Fact]
    public async Task Discarded_redo_and_evicted_history_release_only_owned_backup_files()
    {
        var a = Write(Source, "a", "incoming"); var b = Write(Target, "a", "original");
        var ops = new TestOperations(_root); var stack = new FileUndoStack();
        async Task<FileReplacement> Replace()
        {
            var result = await WindowsFileTransfer.RunAsync(ops, [new(a, b)], false,
                (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
            Assert.Empty(result.Errors); stack.Push(result.Undo!); return Assert.Single(result.Undo!.Replacements);
        }
        var first = await Replace(); stack.TryUndo(ops);
        var saved = first.Backup;
        stack.Push(FileUndoRecord.Created(["unrelated"]));
        Assert.False(File.Exists(saved)); Assert.Equal("original", File.ReadAllText(b));
        var second = await Replace(); saved = second.Backup;
        for (var i = 0; i < FileUndoStack.Limit; i++) stack.Push(FileUndoRecord.Created(["unrelated"]));
        Assert.False(File.Exists(saved)); Assert.Equal("incoming", File.ReadAllText(b));
    }

    [Fact]
    public async Task Replacement_move_across_volumes_keeps_undo_and_redo()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "replace-cross-volume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var a = Write(Source, "cross.txt", "incoming"); var b = Write(fixture, "cross.txt", "original");
            var result = await WindowsFileTransfer.RunAsync(_operations, [new(a, b)], true,
                (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
            Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors));
            Assert.False(File.Exists(a)); Assert.Equal("incoming", File.ReadAllText(b));
            FileUndoApplier.Undo(_operations, result.Undo!);
            Assert.Equal("incoming", File.ReadAllText(a)); Assert.Equal("original", File.ReadAllText(b));
            FileUndoApplier.Redo(_operations, result.Undo!);
            Assert.False(File.Exists(a)); Assert.Equal("incoming", File.ReadAllText(b));
            Assert.Single(result.Undo!.Replacements).Dispose();
        }
        finally { Directory.Delete(fixture, recursive: true); }
    }

    [Fact]
    public async Task Replacing_a_read_only_target_never_removes_its_protection_or_source()
    {
        var a = Write(Source, "read-only", "incoming"); var b = Write(Target, "read-only", "original");
        File.SetAttributes(b, FileAttributes.ReadOnly);
        try
        {
            var result = await WindowsFileTransfer.RunAsync(_operations, [new(a, b)], true,
                (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
            Assert.Single(result.Errors); Assert.Empty(result.Completed);
            Assert.Equal("original", File.ReadAllText(b)); Assert.Equal("incoming", File.ReadAllText(a));
            Assert.True((File.GetAttributes(b) & FileAttributes.ReadOnly) != 0);
        }
        finally { File.SetAttributes(b, FileAttributes.Normal); }
    }

    [Fact]
    public async Task Same_file_through_a_directory_alias_does_not_offer_replace()
    {
        var a = Write(Source, "alias.txt", "original");
        var alias = Path.Combine(_root, "alias"); Directory.CreateSymbolicLink(alias, Source);
        try
        {
            var result = await WindowsFileTransfer.RunAsync(_operations, [new(a, Path.Combine(alias, "alias.txt"))], false,
                (conflict, _) => { Assert.False(conflict.CanReplace); Assert.True(conflict.IsSameItem);
                    return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip)); });
            Assert.Empty(result.Errors); Assert.Equal(1, result.Skipped); Assert.Equal("original", File.ReadAllText(a));
        }
        finally { Directory.Delete(alias); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keep_both_numbers_before_the_extension_without_overwriting(bool move)
    {
        var source = Write(Source, "文件.txt", "incoming");
        var target = Write(Target, "文件.txt", "existing");
        Write(Target, "文件 (2).txt", "already numbered");
        var calls = 0;
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], move, (conflict, _) =>
        {
            calls++; Assert.False(conflict.CanMerge); Assert.Equal("文件 (3).txt", conflict.NumberedName);
            return Task.FromResult(new FileConflictChoice(FileConflictAction.KeepBoth));
        });
        Assert.Equal(1, calls); Assert.Empty(result.Errors); Assert.False(result.Cancelled);
        Assert.Equal("existing", File.ReadAllText(target));
        Assert.Equal("already numbered", File.ReadAllText(Path.Combine(Target, "文件 (2).txt")));
        Assert.Equal("incoming", File.ReadAllText(Assert.Single(result.Completed).Destination));
        Assert.Equal(!move, File.Exists(source));
    }

    [Fact]
    public async Task Copying_a_folder_to_itself_offers_numbering_but_never_self_merge()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Source, "v1.2")).FullName;
        Write(folder, "a.txt", "a");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(folder, folder)], false, (conflict, _) =>
        {
            Assert.False(conflict.CanMerge); Assert.Equal("v1.2 (2)", conflict.NumberedName);
            return Task.FromResult(new FileConflictChoice(FileConflictAction.KeepBoth));
        });
        Assert.Empty(result.Errors);
        Assert.Equal("a", File.ReadAllText(Path.Combine(Source, "v1.2 (2)", "a.txt")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(folder, "a.txt")));
    }

    [Fact]
    public async Task Applying_folder_merge_to_all_still_prompts_for_files_and_file_choice_applies_to_remaining_files()
    {
        Write(Source, "sub/a.txt", "incoming a"); Write(Source, "sub/b.txt", "incoming b");
        Write(Target, "sub/a.txt", "existing a"); Write(Target, "sub/b.txt", "existing b");
        var kinds = new List<bool>();
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(Source, Target)], false, (conflict, _) =>
        {
            kinds.Add(conflict.CanMerge);
            return Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.KeepBoth, true));
        });
        Assert.Equal([true, false], kinds);
        Assert.Empty(result.Errors); Assert.Equal(2, result.Completed.Count);
        Assert.Equal("existing a", File.ReadAllText(Path.Combine(Target, "sub/a.txt")));
        Assert.Equal("incoming b", File.ReadAllText(Path.Combine(Target, "sub/b (2).txt")));
        Assert.Equal("incoming a", File.ReadAllText(Path.Combine(Source, "sub/a.txt")));
    }

    [Fact]
    public async Task Skip_in_a_cut_merge_preserves_source_conflicts_and_undo_restores_only_completed_moves()
    {
        Write(Source, "duplicate.txt", "incoming"); Write(Source, "unique.txt", "unique");
        Write(Target, "duplicate.txt", "existing");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(Source, Target)], true,
            (conflict, _) => Task.FromResult(new FileConflictChoice(conflict.CanMerge ? FileConflictAction.Merge : FileConflictAction.Skip)));
        Assert.Equal(1, result.Skipped); Assert.Empty(result.Errors); Assert.False(result.Cancelled);
        Assert.Equal("incoming", File.ReadAllText(Path.Combine(Source, "duplicate.txt")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(Target, "duplicate.txt")));
        FileUndoApplier.Undo(_operations, result.Undo!);
        Assert.Equal("unique", File.ReadAllText(Path.Combine(Source, "unique.txt")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(Target, "duplicate.txt")));
    }

    [Fact]
    public async Task Cancel_conflict_preserves_prior_completed_work_for_undo_and_does_not_process_later_items()
    {
        var first = Write(Source, "first.txt", "first");
        var conflict = Write(Source, "conflict.txt", "incoming");
        Write(Target, "conflict.txt", "existing");
        var last = Write(Source, "last.txt", "last");
        var result = await WindowsFileTransfer.RunAsync(_operations,
            new[] { first, conflict, last }.Select(p => new FilePathPair(p, Path.Combine(Target, Path.GetFileName(p)))).ToArray(), true,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Cancel)));
        Assert.True(result.Cancelled); Assert.Empty(result.Errors); Assert.Single(result.Completed);
        Assert.True(File.Exists(conflict)); Assert.True(File.Exists(last));
        FileUndoApplier.Undo(_operations, result.Undo!);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(Target, "conflict.txt")));
    }

    [Fact]
    public async Task A_target_created_after_the_check_is_prompted_and_preserved()
    {
        var source = Write(Source, "race.txt", "incoming");
        var target = Path.Combine(Target, "race.txt");
        var operations = new TestOperations(_root) { BeforeRename = (_, destination) => File.WriteAllText(destination, "late arrival") };
        var asked = 0;
        var result = await WindowsFileTransfer.RunAsync(operations, [new(source, target)], true, (_, _) =>
        { asked++; return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip)); });
        Assert.Equal(1, asked); Assert.Equal(1, result.Skipped); Assert.Empty(result.Errors);
        Assert.Null(result.Undo); Assert.Equal("incoming", File.ReadAllText(source));
        Assert.Equal("late arrival", File.ReadAllText(target));
    }

    [Fact]
    public async Task Copy_merge_undo_preserves_existing_folders_and_later_files_and_redo_restores_the_copy()
    {
        Write(Source, "new/nested.txt", "copy"); Write(Target, "existing.txt", "keep");
        var operations = new TestOperations(_root);
        var result = await WindowsFileTransfer.RunAsync(operations, [new(Source, Target)], false,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Merge)));
        Assert.Empty(result.Errors);
        Write(Target, "new/later.txt", "later");
        FileUndoApplier.Undo(operations, result.Undo!);
        Assert.False(File.Exists(Path.Combine(Target, "new/nested.txt")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Target, "existing.txt")));
        Assert.Equal("later", File.ReadAllText(Path.Combine(Target, "new/later.txt")));
        FileUndoApplier.Redo(operations, result.Undo!);
        Assert.Equal("copy", File.ReadAllText(Path.Combine(Target, "new/nested.txt")));
    }

    [Fact]
    public async Task No_resolver_cancels_a_conflict_instead_of_silently_skipping_or_numbering()
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "existing");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false);
        Assert.True(result.Cancelled); Assert.Empty(result.Completed); Assert.Null(result.Undo);
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Fact]
    public async Task Physical_descendant_through_a_junction_is_rejected_before_creating_output()
    {
        var alias = Path.Combine(_root, "alias"); Directory.CreateSymbolicLink(alias, Source);
        try
        {
            var result = await WindowsFileTransfer.RunAsync(_operations, [new(Source, Path.Combine(alias, "nested"))], false);
            Assert.Single(result.Errors); Assert.False(Directory.Exists(Path.Combine(Source, "nested")));
        }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public async Task Copy_preserves_alternate_streams_and_timestamps_without_leaving_staging_files()
    {
        var source = Write(Source, "metadata.txt", "data");
        File.WriteAllText(source + ":test-stream", "metadata");
        var stamp = new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        var target = Path.Combine(Target, "metadata.txt");
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false);
        Assert.Empty(result.Errors);
        Assert.Equal("metadata", File.ReadAllText(target + ":test-stream"));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(target));
        Assert.DoesNotContain(Directory.EnumerateFiles(Target), p => Path.GetFileName(p).StartsWith(".filesmate-copy-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_a_choice_does_not_become_an_error_or_copy_anything()
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "existing");
        using var cancellation = new CancellationTokenSource();
        var result = await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false, (_, token) =>
        {
            cancellation.Cancel(); return Task.FromCanceled<FileConflictChoice>(token);
        }, token: cancellation.Token);
        Assert.True(result.Cancelled); Assert.Empty(result.Errors); Assert.Empty(result.Completed);
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Fact]
    public async Task Remembered_choices_do_not_leak_into_the_next_transfer()
    {
        var source = Write(Source, "a", "incoming"); var target = Write(Target, "a", "existing");
        var calls = 0;
        Task<FileConflictChoice> Choose(FileConflict _, CancellationToken __)
        { calls++; return Task.FromResult(new FileConflictChoice(FileConflictAction.Skip, true)); }
        await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false, Choose);
        await WindowsFileTransfer.RunAsync(_operations, [new(source, target)], false, Choose);
        Assert.Equal(2, calls);
    }

    private static string Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content); return path;
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Disk-backed recycling substitute keeps test undo data out of the user's Recycle Bin.
    private sealed class TestOperations(string root) : ILocalFileOperations
    {
        private readonly WindowsLocalFileOperations _inner = new();
        private readonly Dictionary<string, string> _recycled = [];
        public Action<string, string>? BeforeRename { get; init; }
        public void Rename(string source, string target) { BeforeRename?.Invoke(source, target); _inner.Rename(source, target); }
        public bool TryRenameFileWithoutCopy(string source, string target)
        { BeforeRename?.Invoke(source, target); return _inner.TryRenameFileWithoutCopy(source, target); }
        public void CreateDirectory(string path, bool failIfExists = false) => _inner.CreateDirectory(path, failIfExists);
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null)
        { foreach (var path in paths) { var saved = Path.Combine(root, Guid.NewGuid().ToString("N")); File.Move(path, saved); _recycled.Add(path, saved); } }
        public void RestoreRecycled(IReadOnlyList<string> paths)
        { foreach (var path in paths) File.Move(_recycled[path], path); }
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
