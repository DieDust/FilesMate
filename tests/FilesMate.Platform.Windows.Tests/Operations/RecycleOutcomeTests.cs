using FilesMate.Core.Operations;
using FilesMate.Platform.Windows.Operations;

namespace FilesMate.Platform.Windows.Tests.Operations;

public sealed class RecycleOutcomeTests
{
    [Fact]
    public void RecyclingAFolderReportsOneRecoverableRootAndPreservesItsContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilesMateRecycle", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "folder");
        var file = Path.Combine(folder, "nested", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "preserved");
        var operations = new WindowsLocalFileOperations();
        var completed = new List<RecycleItemResult>();
        try
        {
            operations.Recycle([folder], completed.Add);
            Assert.False(Directory.Exists(folder));
            Assert.Equal(new RecycleItemResult(folder, true), Assert.Single(completed));
            operations.RestoreRecycled([folder]);
            Assert.Equal("preserved", File.ReadAllText(file));
        }
        finally
        {
            // Only this test's unique directory is removed.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0x00270008, true)] // Completed recycle move
    [InlineData(0x00270005, false)] // User ignored / skipped
    [InlineData(0x0027000B, false)] // Pending
    [InlineData(0x00270010, false)] // Pending deletion
    [InlineData(unchecked((int)0x80270000), false)] // Cancelled
    [InlineData(unchecked((int)0x80070005), false)] // Access denied
    public void OnlyCompletedDeletionCanCreateAnUndoRecord(int result, bool completed)
    {
        Assert.Equal(completed, WindowsRecycleOperation.IsCompletedDeletion(result));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(117)]
    public void AbortedShellOperationIsNeverSuccess(int result)
    {
        Assert.Throws<OperationCanceledException>(() => WindowsRecycleOperation.VerifyCompletion(result, true, []));
    }

    [Fact]
    public void SuccessfulReturnWithRemainingFilesIsNotComplete()
    {
        Assert.Throws<IOException>(() => WindowsRecycleOperation.VerifyCompletion(0, false, [true, false]));
    }
}
