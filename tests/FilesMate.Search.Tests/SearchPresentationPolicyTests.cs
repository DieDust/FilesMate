using FilesMate.SearchHost;

namespace FilesMate.Search.Tests;

public sealed class SearchPresentationPolicyTests
{
    [Theory]
    [InlineData("", true, true, 3, 0)]
    [InlineData("  ", false, false, 0, 0)]
    [InlineData("files", true, false, 3, 1)]
    [InlineData("files", true, true, 3, 2)]
    [InlineData("files", false, true, 3, 2)]
    [InlineData("files", false, true, 0, 3)]
    public void StateDoesNotTreatPreviousQueryRowsAsCurrent(string query, bool pending, bool accepted, int count, int expected)
        => Assert.Equal((SearchPresentationState)expected, SearchPresentationPolicy.Resolve(query, pending, accepted, count));

    [Fact]
    public void PartialReorderKeepsSelectedIdentityRatherThanIndex()
        => Assert.Equal(new[] { "file-b" }, SearchPresentationPolicy.RetainSelection(
            new[] { "app", "file-c", "file-b" }, new[] { "file-b" }, false));

    [Fact]
    public void RemovedSelectionDoesNotFallBackToAnotherTarget()
        => Assert.Empty(SearchPresentationPolicy.RetainSelection(new[] { "other-file" }, new[] { "removed-file" }, false));

    [Fact]
    public void FirstUsefulBatchHasOneDefaultSelection()
        => Assert.Equal(new[] { "first" }, SearchPresentationPolicy.RetainSelection(new[] { "first", "second" }, new[] { "old" }, true));

    [Theory]
    [InlineData("ReadMe FILES.txt", "files", "FILES")]
    [InlineData("年度报告.txt", "报告", "报告")]
    [InlineData("<Border Path='x'/>.txt", "Border", "Border")]
    [InlineData("budget final.txt", "budget final", "budget|final")]
    public void MatchesAreLiteralCaseInsensitiveAndSupportMultipleWords(string text, string query, string matched)
    {
        var spans = SearchPresentationPolicy.MatchSpans(text, query);
        Assert.Equal(text, string.Concat(spans.Select(span => text.Substring(span.Start, span.Length))));
        Assert.Equal(matched, string.Join("|", spans.Where(span => span.Matched).Select(span => text.Substring(span.Start, span.Length))));
    }

    [Fact]
    public void MalformedOversizedQueryHasNoHighlightWork()
        => Assert.Equal(new[] { new SearchMatchSpan(0, 8, false) }, SearchPresentationPolicy.MatchSpans("safe.txt", new string('x', 2049)));

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, false)]
    public void TerminalStatusNeverMarksAnIncompleteResponseAsComplete(bool final, bool incomplete, bool failed, bool completed)
    {
        Assert.Equal(failed, SearchPresentationPolicy.FailedCompletion(final, incomplete));
        Assert.Equal(completed, SearchPresentationPolicy.SuccessfulCompletion(final, incomplete));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IncompleteCompletionKeepsUsableRowsButAlwaysReportsFailure(int count)
    {
        var state = SearchPresentationPolicy.Resolve("report", false, true, count);
        Assert.Equal(count > 0 ? SearchPresentationState.Results : SearchPresentationState.EmptyOrError, state);
        Assert.True(SearchPresentationPolicy.FailedCompletion(true, true));
        Assert.False(SearchPresentationPolicy.ShowCompleted(state, false, false, true));
    }

    [Theory]
    [InlineData(10, 10, false, false, true)]
    [InlineData(11, 10, false, false, false)]
    [InlineData(10, 10, true, false, false)]
    [InlineData(10, 10, false, true, false)]
    [InlineData(11, 10, true, true, false)]
    [InlineData(10, -1, false, false, false)]
    public void CurrentGenerationAndImeGateEveryResultAction(long generation, long accepted, bool pending, bool composing, bool expected)
        => Assert.Equal(expected, SearchPresentationPolicy.HasCurrentResults(generation, accepted, pending, composing));

    [Theory]
    [InlineData(2, true, false, false, true)]
    [InlineData(2, false, false, false, false)]
    [InlineData(2, true, true, false, false)]
    [InlineData(2, true, false, true, false)]
    [InlineData(1, true, false, false, false)]
    [InlineData(0, true, false, false, false)]
    [InlineData(3, true, false, false, false)]
    public void CompletionFooterRequiresSuccessfulTerminalResponseAndNoActiveLoad(int state, bool completed, bool loading, bool failed, bool expected)
        => Assert.Equal(expected, SearchPresentationPolicy.ShowCompleted((SearchPresentationState)state, completed, loading, failed));

    [Fact]
    public void CompositionImmediatelyHidesAnAcceptedPreviousQuery()
    {
        var accepted = 10L;
        Assert.Equal(SearchPresentationState.Results, SearchPresentationPolicy.Resolve("report", false,
            SearchPresentationPolicy.HasCurrentResults(10, accepted, false, false), 2));
        Assert.Equal(SearchPresentationState.Searching, SearchPresentationPolicy.Resolve("report", true,
            SearchPresentationPolicy.HasCurrentResults(11, accepted, true, true), 2));
        Assert.Equal(SearchPresentationState.Default, SearchPresentationPolicy.Resolve("", true,
            SearchPresentationPolicy.HasCurrentResults(11, accepted, true, true), 2));
    }

    [Fact]
    public void EmptyFirstBatchDoesNotInventASelection()
        => Assert.Empty(SearchPresentationPolicy.RetainSelection(Array.Empty<string>(), new[] { "old" }, true));

    [Fact]
    public void MultiselectionSurvivesPartialReorderingByIdentity()
        => Assert.Equal(new[] { "file-c", "file-b" }, SearchPresentationPolicy.RetainSelection(
            new[] { "file-c", "new-file", "file-b" }, new[] { "file-b", "file-c" }, false));
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "informational notice", false)]
    [InlineData(false, "SearchPage_IndexMissing", true)]
    [InlineData(false, "SearchPage_MetadataRequired", true)]
    [InlineData(false, "Search_IndexUnavailable", true)]
    [InlineData(false, "Search_PartialUnavailable", true)]
    [InlineData(false, "Search_PartialTimedOut", true)]
    [InlineData(true, null, true)]
    public void KnownMissingSourcesNeverPretendToBeACompleteSearch(bool incomplete, string? notice, bool expected)
        => Assert.Equal(expected, SearchPresentationPolicy.IncompleteResponse(incomplete, notice));

}
