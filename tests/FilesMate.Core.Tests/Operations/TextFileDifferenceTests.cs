using FilesMate.Core.Operations;

namespace FilesMate.Core.Tests.Operations;

public sealed class TextFileDifferenceTests
{
    [Fact]
    public void Insertions_and_removals_do_not_shift_every_following_line()
    {
        var result = TextFileDifference.Compare("header\nnew\nkeep\ntail", "header\nkeep\nold\ntail");
        Assert.Equal(1, result.AddedLines); Assert.Equal(1, result.RemovedLines);
        Assert.Contains(result.Rows, row => row.Incoming == "keep" && row.Existing == "keep" && row.Kind == TextDifferenceKind.Equal);
        Assert.Contains(result.Rows, row => row.Incoming == "new" && row.ExistingLine is null);
        Assert.Contains(result.Rows, row => row.Existing == "old" && row.IncomingLine is null);
    }

    [Fact]
    public void Changed_lines_are_aligned_and_keep_original_line_numbers()
    {
        var result = TextFileDifference.Compare("a\nnew\nc", "a\nold\nc");
        var row = Assert.Single(result.Rows, r => r.Kind == TextDifferenceKind.Changed);
        Assert.Equal(2, row.IncomingLine); Assert.Equal(2, row.ExistingLine);
        Assert.Equal("new", row.Incoming); Assert.Equal("old", row.Existing);
    }

    [Fact]
    public void Empty_files_and_final_newlines_are_handled()
    {
        Assert.Empty(TextFileDifference.Compare("", "").Rows);
        Assert.Equal(1, TextFileDifference.Compare("a", "").AddedLines);
        Assert.Equal(1, TextFileDifference.Compare("a\n", "a").AddedLines);
    }

    [Fact]
    public void Common_regions_are_collapsed_without_losing_change_line_numbers()
    {
        var lines = Enumerable.Range(1, 100).Select(i => i.ToString()).ToArray();
        var previous = string.Join('\n', lines); lines[49] = "changed";
        var result = TextFileDifference.Compare(string.Join('\n', lines), previous);
        Assert.True(result.Rows.Count < 20);
        Assert.Contains(result.Rows, r => r.IncomingLine == 50 && r.Kind == TextDifferenceKind.Changed);
        Assert.Contains(result.Rows, r => r.Kind == TextDifferenceKind.Omitted);
    }

    [Fact]
    public void Huge_unrelated_regions_are_bounded_and_explicitly_marked()
    {
        var result = TextFileDifference.Compare(string.Join('\n', Enumerable.Repeat("a", 2500)), string.Join('\n', Enumerable.Repeat("b", 2500)));
        Assert.True(result.IsLimited); Assert.True(result.IsSimplified);
        Assert.Equal(2000, result.AddedLines); Assert.Equal(2000, result.Rows.Count);
        Assert.Throws<OperationCanceledException>(() => TextFileDifference.Compare("a", "b", new(true)));
    }

    [Fact]
    public void Truncating_a_long_line_never_hides_the_limited_status()
    {
        var result = TextFileDifference.Compare(new string('a', 5000), new string('a', 5000));
        Assert.True(result.IsLimited); Assert.True(result.Rows[0].Incoming.Length < 5000);
    }
}
