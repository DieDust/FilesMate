using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class FileTreeSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FilesMateSafetyTests", Guid.NewGuid().ToString("N"));
    public FileTreeSafetyTests() { Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, ".test-root"), "FilesMate"); }

    [Fact]
    public void Copy_into_descendant_is_rejected_before_creating_destination()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "keep.txt"), "keep");
        var target = Path.Combine(source, "child");
        Assert.Throws<IOException>(() => new WindowsLocalFileOperations().Copy([source], target));
        Assert.False(Directory.Exists(target));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(source, "keep.txt")));
    }

    [Fact]
    public void Destination_alias_into_source_is_rejected()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, source);
        Assert.Throws<IOException>(() => new WindowsLocalFileOperations().Copy([source], Path.Combine(alias, "child")));
        Assert.False(Directory.Exists(Path.Combine(source, "child")));
    }

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("C:\\first.txt\0C:\\second.txt")]
    public void Invalid_shell_paths_are_rejected_before_operation(string path)
    {
        Assert.Throws<ArgumentException>(() => new WindowsLocalFileOperations().PermanentDelete([path]));
    }

    [Fact]
    public async Task Shell_work_runs_on_separate_sta_and_propagates_failure()
    {
        var caller = Environment.CurrentManagedThreadId;
        var worker = 0;
        var apartment = ApartmentState.Unknown;
        await ShellOperationWorker.RunAsync(() => { worker = Environment.CurrentManagedThreadId; apartment = Thread.CurrentThread.GetApartmentState(); });
        Assert.NotEqual(caller, worker);
        Assert.Equal(ApartmentState.STA, apartment);
        await Assert.ThrowsAsync<IOException>(() => ShellOperationWorker.RunAsync(() => throw new IOException("test")));
    }

    [Fact]
    public async Task Waiting_shell_activation_does_not_block_caller_or_other_shell_work()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ShellOperationWorker.RunAsync(() =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            await ShellOperationWorker.RunAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
        }
        finally { release.Set(); await pending; }
    }

    [Fact]
    public void Managed_delete_of_directory_link_preserves_target()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, target);
        WindowsLocalFileOperations.DeleteManaged(link);
        Assert.False(Path.Exists(link));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void Copy_does_not_follow_directory_link_cycles()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(source, "loop"), source);
        Assert.Throws<IOException>(() => new WindowsLocalFileOperations().Copy([source], Path.Combine(_root, "destination")));
        Assert.False(Directory.Exists(Path.Combine(_root, "destination", "source", "loop")));
    }

    [Fact]
    public void Managed_delete_handles_file_links_without_deleting_target()
    {
        var outside = Path.Combine(_root, "keep.txt");
        File.WriteAllText(outside, "keep");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "remove")).FullName;
        File.CreateSymbolicLink(Path.Combine(folder, "link.txt"), outside);
        WindowsLocalFileOperations.DeleteManaged(folder);
        Assert.False(Directory.Exists(folder));
        Assert.Equal("keep", File.ReadAllText(outside));
    }

    public void Dispose()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FilesMateSafetyTests")) + Path.DirectorySeparatorChar;
        if (!_root.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(_root, ".test-root")))
            throw new InvalidOperationException("Refusing unmarked cleanup.");
        Directory.Delete(_root, true); // .NET deletes symbolic links themselves rather than traversing their targets.
    }
}
