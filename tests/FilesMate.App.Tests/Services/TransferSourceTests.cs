using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class TransferSourceTests
{
    [Fact]
    public void SelectedParentSuppressesOnlyItsDescendantsAndRetainsOrder()
    {
        var roots = FileShelfTransfer.SelectRoots([@"C:\test\folder\child", @"C:\test\other", @"C:\test\folder\",
            @"C:\test\folder2", @"C:\test\FOLDER", @"C:\test\folder\deep\item"]);
        Assert.Equal(new[] { @"C:\test\other", @"C:\test\folder", @"C:\test\folder2" }, roots);
    }

    [Fact]
    public void LargeSiblingSelectionDoesNotLoseItems()
    {
        var paths = Enumerable.Range(0, 20000).Select(i => @"C:\items\file" + i).ToArray();
        Assert.Equal(paths, FileShelfTransfer.SelectRoots(paths));
    }
}
