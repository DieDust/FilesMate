using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class UniquePathTests
{
    [Fact]
    public void First_candidate_is_used_when_the_path_is_free()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = UniquePath.CombineAvailable(@"D:\inbox", "New folder", taken.Contains);
        Assert.Equal(@"D:\inbox\New folder", path);
    }

    [Fact]
    public void Duplicate_names_gain_a_numeric_suffix_before_the_extension()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\inbox\New file.txt",
            @"D:\inbox\New file (2).txt",
        };

        var path = UniquePath.CombineAvailable(@"D:\inbox", "New file.txt", taken.Contains);
        Assert.Equal(@"D:\inbox\New file (3).txt", path);
    }
}
