using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class WindowsCrossVolumeTransferTests : IDisposable
{
    [Fact]
    public async Task Failed_copy_callback_does_not_publish_a_partial_move_or_remove_source()
    {
        var source = NewSource(8L * 1024 * 1024, false);
        var target = Path.Combine(_targetRoot, "interrupted.bin");
        var result = await WindowsFileTransfer.RunAsync(new StreamedMoveOperations(), [new(source, target)], true,
            byteProgress: new InlineProgress(_ => throw new IOException("Simulated device I/O failure")));
        Assert.Single(result.Errors);
        Assert.Empty(result.Completed);
        Assert.Null(result.Undo);
        Assert.Equal(8L * 1024 * 1024, new FileInfo(source).Length);
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFileSystemEntries(_targetRoot));
    }
    private readonly string _sourceRoot = Directory.CreateTempSubdirectory("FilesMate-cross-volume-").FullName;
    private readonly string _targetRoot = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
        "FilesMate-cross-volume-" + Guid.NewGuid().ToString("N"))).FullName;
    private bool HasDifferentVolumes => !string.Equals(Path.GetPathRoot(_sourceRoot), Path.GetPathRoot(_targetRoot), StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceling_streamed_move_keeps_source_and_removes_only_its_staging(bool readOnly)
    {
        var source = NewSource(32L * 1024 * 1024, readOnly);
        var target = Path.Combine(_targetRoot, "incoming.bin");
        var unrelated = Path.Combine(_targetRoot, "keep.txt");
        File.WriteAllText(unrelated, "keep");
        using var cancel = new CancellationTokenSource();
        long observed = 0;
        var result = await WindowsFileTransfer.RunAsync(MoveOperations(), [new(source, target)], true,
            token: cancel.Token, byteProgress: new InlineProgress(p => { observed = p.Transferred; cancel.Cancel(); }));
        Assert.True(result.Cancelled);
        Assert.Empty(result.Completed);
        Assert.Empty(result.Errors);
        Assert.Null(result.Undo);
        Assert.InRange(observed, 1, new FileInfo(source).Length - 1);
        Assert.False(File.Exists(target));
        Assert.Equal("keep", File.ReadAllText(unrelated));
        Assert.Equal([unrelated], Directory.GetFileSystemEntries(_targetRoot));
        Assert.Equal(32L * 1024 * 1024, new FileInfo(source).Length);
        Assert.Equal(readOnly, (File.GetAttributes(source) & FileAttributes.ReadOnly) != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streamed_move_preserves_attributes_streams_and_timestamps_before_removing_source(bool readOnly)
    {
        var source = NewSource(1024, false);
        File.WriteAllText(source + ":metadata", "alternate stream");
        var stamp = new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        if (readOnly) File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
        var target = Path.Combine(_targetRoot, "incoming.bin");
        long observed = 0;
        var result = await WindowsFileTransfer.RunAsync(MoveOperations(), [new(source, target)], true,
            byteProgress: new InlineProgress(p => observed = p.Transferred));
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.Single(result.Completed);
        Assert.False(File.Exists(source));
        Assert.True(observed >= 1024);
        Assert.Equal(1024, new FileInfo(target).Length);
        Assert.Equal("alternate stream", File.ReadAllText(target + ":metadata"));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(target));
        Assert.Equal(readOnly, (File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
        Assert.Equal([target], Directory.GetFileSystemEntries(_targetRoot));
    }

    [Fact]
    public async Task Changed_source_metadata_is_not_deleted_or_published()
    {
        var source = NewSource(32L * 1024 * 1024, false);
        var target = Path.Combine(_targetRoot, "incoming.bin");
        var changed = false;
        var result = await WindowsFileTransfer.RunAsync(new StreamedMoveOperations(), [new(source, target)], true,
            byteProgress: new InlineProgress(_ =>
            {
                if (changed) return;
                File.SetLastWriteTimeUtc(source, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                changed = true;
            }));
        Assert.True(changed);
        Assert.Single(result.Errors);
        Assert.Empty(result.Completed);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFileSystemEntries(_targetRoot));
    }

    [Fact]
    public void Source_lease_rejects_same_name_replacement_and_never_deletes_it()
    {
        var source = NewSource(1024, false);
        var relocated = Path.Combine(_sourceRoot, "relocated.bin");
        using var lease = new WindowsFileMove.SourceLease(source);
        File.Move(source, relocated);
        File.WriteAllText(source, "a different file");
        Assert.Throws<IOException>(() => lease.ValidatePath(source));
        Assert.Equal("a different file", File.ReadAllText(source));
        Assert.Equal(1024, new FileInfo(relocated).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_cross_volume_replacement_cancellation_preserves_both_versions_when_available(bool retainUndo)
    {
        // CI may have only one volume; the isolated Windows verification places output on D: and TEMP on C:.
        if (!HasDifferentVolumes) return;
        var source = NewSource(32L * 1024 * 1024, false);
        var target = Path.Combine(_targetRoot, "incoming.bin");
        File.WriteAllText(target, "previous version");
        using var cancel = new CancellationTokenSource();
        long observed = 0;
        var result = await WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)], true,
            (_, _) => Task.FromResult(new FileConflictChoice(retainUndo ? FileConflictAction.Replace : FileConflictAction.ReplaceWithoutUndo)),
            token: cancel.Token, byteProgress: new InlineProgress(p => { observed = p.Transferred; cancel.Cancel(); }));
        Assert.True(result.Cancelled);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Completed);
        Assert.Null(result.Undo);
        Assert.InRange(observed, 1, new FileInfo(source).Length - 1);
        Assert.Equal("previous version", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFileSystemEntries(_targetRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_replacement_rejection_of_readonly_source_keeps_both_originals(bool retainUndo)
    {
        if (!HasDifferentVolumes) return;
        var source = NewSource(1024, true);
        var target = Path.Combine(_targetRoot, "incoming.bin");
        File.WriteAllText(target, "previous version");
        var result = await WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)], true,
            (_, _) => Task.FromResult(new FileConflictChoice(retainUndo ? FileConflictAction.Replace : FileConflictAction.ReplaceWithoutUndo)));
        // ReplaceFile also rejects a read-only replacement operand. Retain that protection.
        Assert.Single(result.Errors);
        Assert.Empty(result.Completed);
        Assert.True(File.Exists(source));
        Assert.Equal("previous version", File.ReadAllText(target));
        Assert.True((File.GetAttributes(source) & FileAttributes.ReadOnly) != 0);
        Assert.Equal([target], Directory.GetFileSystemEntries(_targetRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Readonly_target_is_never_silently_overwritten(bool move)
    {
        var source = NewSource(1024, false);
        var target = Path.Combine(_targetRoot, "incoming.bin");
        File.WriteAllText(target, "previous version");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        var result = await WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)], move,
            (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.Replace)));
        Assert.Single(result.Errors);
        Assert.Empty(result.Completed);
        Assert.True(File.Exists(source));
        Assert.Equal("previous version", File.ReadAllText(target));
        Assert.True((File.GetAttributes(target) & FileAttributes.ReadOnly) != 0);
        Assert.Equal([target], Directory.GetFileSystemEntries(_targetRoot));
    }

    private string NewSource(long bytes, bool readOnly)
    {
        var source = Path.Combine(_sourceRoot, "incoming.bin");
        using (var file = File.Create(source)) file.SetLength(bytes);
        if (readOnly) File.SetAttributes(source, FileAttributes.ReadOnly);
        return source;
    }

    private ILocalFileOperations MoveOperations() => HasDifferentVolumes ? new WindowsLocalFileOperations() : new StreamedMoveOperations();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_primary_stream_and_alternate_streams_can_be_moved(bool hasAlternateStream)
    {
        var source = NewSource(0, false);
        if (hasAlternateStream) File.WriteAllText(source + ":metadata", "metadata only");
        var target = Path.Combine(_targetRoot, "incoming.bin");
        var result = await WindowsFileTransfer.RunAsync(MoveOperations(), [new(source, target)], true);
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.Single(result.Completed);
        Assert.False(File.Exists(source));
        Assert.Equal(0, new FileInfo(target).Length);
        if (hasAlternateStream) Assert.Equal("metadata only", File.ReadAllText(target + ":metadata"));
    }

    [Fact]
    public void Copy_validates_its_actual_source_handle_against_the_leased_file()
    {
        var source = NewSource(1024, false);
        var renamed = Path.Combine(_sourceRoot, "leased.bin");
        var staging = Path.Combine(_targetRoot, "staging.bin");
        using var lease = new WindowsFileMove.SourceLease(source);
        File.Move(source, renamed);
        File.WriteAllText(source, "a different object at the original path");
        using var reservation = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Throws<IOException>(() => WindowsFileCopy.Copy(source, staging, staging, CancellationToken.None,
            validateSource: lease.ValidateCopyHandle));
        Assert.Equal(1024, new FileInfo(renamed).Length);
        Assert.Equal("a different object at the original path", File.ReadAllText(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Running_source_cannot_be_removed_and_preserves_recoverable_versions_when_available(bool replace)
    {
        if (!HasDifferentVolumes) return;
        var source = Path.Combine(_sourceRoot, "running.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), source);
        var target = Path.Combine(_targetRoot, "running.exe");
        if (replace) File.WriteAllText(target, "previous version");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(source)
        {
            Arguments = "/d /c pause", UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        try
        {
            var result = await WindowsFileTransfer.RunAsync(new WindowsLocalFileOperations(), [new(source, target)], true,
                (_, _) => Task.FromResult(new FileConflictChoice(FileConflictAction.ReplaceWithoutUndo)));
            Assert.Single(result.Errors);
            Assert.Empty(result.Completed);
            Assert.True(File.Exists(source));
            if (replace)
            {
                Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
                var backupDirectory = Assert.Single(Directory.GetDirectories(_targetRoot, ".filesmate-history-*"));
                Assert.False((File.GetAttributes(backupDirectory) & FileAttributes.Hidden) != 0);
                var backup = Path.Combine(backupDirectory, "running.exe");
                Assert.Equal("previous version", File.ReadAllText(backup));
                Assert.Contains(backup, result.Errors[0], StringComparison.Ordinal);
                Assert.Contains(target, result.Errors[0], StringComparison.Ordinal);
            }
            else Assert.Empty(Directory.GetFileSystemEntries(_targetRoot));
        }
        finally
        {
            process.StandardInput.WriteLine();
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeout.Token);
        }
    }

    public void Dispose()
    {
        foreach (var root in new[] { _sourceRoot, _targetRoot })
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class InlineProgress(Action<FileCopyProgress> action) : IProgress<FileCopyProgress>
    {
        public void Report(FileCopyProgress value) => action(value);
    }

    // Exercises the streamed branch even on runners with one writable volume.
    private sealed class StreamedMoveOperations : ILocalFileOperations
    {
        public void CreateDirectory(string path, bool failIfExists = false) => new WindowsLocalFileOperations().CreateDirectory(path, failIfExists);
        public void Rename(string source, string destinationPath) => throw new NotSupportedException();
        public void CreateEmptyFile(string path) => throw new NotSupportedException();
        public IReadOnlyList<string> Copy(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public IReadOnlyList<string> Move(IReadOnlyList<string> sources, string destinationDirectory) => throw new NotSupportedException();
        public void Recycle(IReadOnlyList<string> paths, Action<RecycleItemResult>? completed = null) => throw new NotSupportedException();
        public void RestoreRecycled(IReadOnlyList<string> originalPaths) => throw new NotSupportedException();
        public void PermanentDelete(IReadOnlyList<string> paths) => throw new NotSupportedException();
        public void ShowProperties(string path) => throw new NotSupportedException();
    }
}
