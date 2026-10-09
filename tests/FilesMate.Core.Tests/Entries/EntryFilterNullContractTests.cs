using FilesMate.Core.Entries;

namespace FilesMate.Core.Tests.Entries;

public sealed class EntryFilterNullContractTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".")]
    [InlineData("txt")]
    public void Extension_query_preserves_original_default_snapshot_exception(string query)
    {
        FileEntryCore entry = default;
        var filter = new EntryFilter { Query = query, Match = FilterMatchKind.Extension };
        var original = Record.Exception(() => OriginalHasExtension(entry.Name, query));
        var candidate = Record.Exception(() => filter.Matches(entry));
        Assert.IsType<NullReferenceException>(original);
        Assert.IsType<NullReferenceException>(candidate);
    }

    [Fact]
    public void Empty_query_does_not_inspect_default_snapshot_name()
    {
        Assert.True(new EntryFilter { Match = FilterMatchKind.Extension }.Matches(default));
    }

    private static bool OriginalHasExtension(string name, string query)
    {
        var extension = Path.GetExtension(name);
        if (query.StartsWith('.')) return extension.Equals(query, StringComparison.OrdinalIgnoreCase);
        return extension.Equals("." + query, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(query, StringComparison.OrdinalIgnoreCase);
    }
}
