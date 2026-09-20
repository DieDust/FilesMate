using FilesMate.Platform.Windows.Locks;

namespace FilesMate.Platform.Windows.Tests.Locks;

public sealed class FileLockQueryTests
{
    [Fact]
    public void HandlesFor_selects_the_process_or_one_locked_path()
    {
        var process = new FileLockProcess(
            12,
            "python.exe",
            @"C:\Python\python.exe",
            null,
            true,
            [@"C:\a.pdf", @"C:\b.pdf"],
            [
                new FileLockHandle(12, 1, @"C:\a.pdf"),
                new FileLockHandle(12, 2, @"C:\b.pdf"),
                new FileLockHandle(12, 3, @"C:\a.pdf"),
            ]);

        Assert.Equal(3, FileLockQuery.HandlesFor(process, null).Count);
        Assert.Equal(2, FileLockQuery.HandlesFor(process, @"C:\a.pdf").Count);
        Assert.All(
            FileLockQuery.HandlesFor(process, @"C:\a.pdf"),
            handle => Assert.True(FileLockPath.Matches(handle.Path, @"C:\a.pdf", directory: false)));
        Assert.Single(FileLockQuery.HandlesFor(process, @"C:\b.pdf"));
    }
}
