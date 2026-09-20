using FilesMate.App.Navigation;
using FilesMate.Core.Entries;

namespace FilesMate.App.Tests.Navigation;

public sealed class TagFilterStateTests
{
    [Fact]
    public void Tag_filter_matches_only_the_published_entry_ids()
    {
        var filter = new TagFilterState("Work", new HashSet<int> { 2, 4 });

        Assert.False(filter.Matches(new FileEntryCore(1, "a.txt", 0, 0, 0, FileAttributes.Normal, EntryKind.File)));
        Assert.True(filter.Matches(new FileEntryCore(2, "b.txt", 0, 0, 0, FileAttributes.Normal, EntryKind.File)));
        Assert.Equal("Work", filter.Label);
    }
}
