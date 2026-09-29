using FilesMate.App.Models;
using FilesMate.Core.Operations;

namespace FilesMate.App.Tests.Services;

public sealed class OperationHistoryTests
{
    [Fact]
    public void Renamed_and_moved_files_use_the_current_side_of_history_for_open_and_locate()
    {
        var record = FileUndoRecord.Relocated([new(@"D:\before.txt", @"D:\after.txt")]);
        Assert.Equal([@"D:\after.txt"], new OperationHistoryEntry(record, false).Paths);
        Assert.Equal([@"D:\before.txt"], new OperationHistoryEntry(record, true).Paths);
    }

    [Fact]
    public void Copied_folders_and_files_remain_available_in_the_history_file_picker()
    {
        var record = FileUndoRecord.Copied([@"D:\copy\a.txt", @"D:\copy\b.txt"], [@"D:\copy"]);
        Assert.Equal(3, new OperationHistoryEntry(record, false).Paths.Count);
    }
}
