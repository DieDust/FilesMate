using FilesMate.App.Models;

namespace FilesMate.App.Services;

public interface IFileNameSearchIndex : IAsyncDisposable
{
    public string FilePath { get; }

    public bool IsRunning { get; }

    public SearchIndexStats Stats { get; }

    public event EventHandler<SearchIndexProgress>? ProgressChanged;

    public Task<IReadOnlyList<HomeSearchHit>> SearchAsync(
        string query,
        string? directory = null,
        IReadOnlyList<SearchHitKind>? rankOrder = null,
        CancellationToken cancellationToken = default);

    public Task RebuildAsync(SearchIndexSettings settings, CancellationToken cancellationToken = default);

    public void Cancel();
}
