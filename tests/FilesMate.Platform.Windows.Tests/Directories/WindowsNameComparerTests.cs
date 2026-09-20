using FilesMate.Platform.Windows.Sorting;

namespace FilesMate.Platform.Windows.Tests.Directories;

public sealed class WindowsNameComparerTests
{
    [Fact]
    public void StrCmpLogical_orders_file2_before_file10()
    {
        var comparer = WindowsNameComparer.Instance;
        Assert.True(comparer.Compare("file2", "file10") < 0);
        Assert.True(comparer.Compare("file10", "file2") > 0);
        Assert.Equal(0, comparer.Compare("File", "file"));
    }
}
