using FilesMate.Search;
using FilesMate.SearchHost;

namespace FilesMate.Search.Tests;

public sealed class SearchPresentationProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingIndexFromRealConfiguredProviderIsFailureWithOrWithoutApplicationRows(bool includeApplication)
    {
        // This intentionally nonexistent synthetic profile cannot read a real index.
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-presentation-missing-" + Guid.NewGuid().ToString("N"));
        var catalog = new SyntheticCatalog(includeApplication);
        var provider = new ConfiguredSearchProvider(catalog, profile);
        var updates = new List<GlobalSearchUpdate>();
        await foreach (var update in provider.SearchUpdatesAsync("report", CancellationToken.None)) updates.Add(update);
        var final = Assert.Single(updates, update => update.IsFinal);
        Assert.Equal("SearchPage_IndexMissing", final.Response.Notice);
        Assert.Equal(includeApplication ? 1 : 0, final.Response.Hits.Count);
        var incomplete = SearchPresentationPolicy.IncompleteResponse(final.IsIncomplete, final.Response.Notice);
        Assert.True(SearchPresentationPolicy.FailedCompletion(final.IsFinal, incomplete));
        Assert.False(SearchPresentationPolicy.SuccessfulCompletion(final.IsFinal, incomplete));
        Assert.Equal(includeApplication ? SearchPresentationState.Results : SearchPresentationState.EmptyOrError,
            SearchPresentationPolicy.Resolve("report", false, true, final.Response.Hits.Count));
    }

    [Fact]
    public async Task FallbackProviderUsesTheSameMissingIndexNoticeAndFailurePolicy()
    {
        var profile = Path.Combine(Path.GetTempPath(), "FilesMate-presentation-fallback-" + Guid.NewGuid().ToString("N"));
        var response = await new IndexedFilesSearchProvider(profile).SearchAsync("report", CancellationToken.None);
        Assert.Equal("SearchPage_IndexMissing", response.Notice);
        Assert.Empty(response.Hits);
        Assert.False(response.HasMore);
        Assert.True(SearchPresentationPolicy.IncompleteResponse(false, response.Notice));
    }

    private sealed class SyntheticCatalog(bool includeApplication) : IApplicationCatalog
    {
        public Task<IReadOnlyList<ApplicationEntry>> GetAsync(CancellationToken token)
            => Task.FromResult<IReadOnlyList<ApplicationEntry>>(includeApplication
                ? [new ApplicationEntry("synthetic-report", "Report app", "synthetic-report.exe")]
                : []);
    }
}
