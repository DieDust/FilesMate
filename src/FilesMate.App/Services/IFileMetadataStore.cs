using FilesMate.App.Models;
using FilesMate.Core.Metadata;

namespace FilesMate.App.Services;

public interface IFileMetadataStore : IAsyncDisposable
{
    public Task<IReadOnlyList<TagDefinition>> ListTagsAsync(CancellationToken cancellationToken = default);

    public Task<TagDefinition> CreateTagAsync(
        string name,
        string color,
        int? sortOrder = null,
        CancellationToken cancellationToken = default);

    public Task<TagDefinition> UpdateTagAsync(
        long tagId,
        string name,
        string color,
        CancellationToken cancellationToken = default);

    public Task ReorderTagsAsync(IReadOnlyList<long> orderedIds, CancellationToken cancellationToken = default);

    public Task DeleteTagAsync(long tagId, CancellationToken cancellationToken = default);

    public Task UpsertFileIdentityAsync(FileIdentity identity, CancellationToken cancellationToken = default);

    public Task<FileIdentity?> GetIdentityAsync(string stableKey, CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<TagDefinition>> GetTagsAsync(
        FileIdentity identity,
        CancellationToken cancellationToken = default);

    public Task SetTagsAsync(
        FileIdentity identity,
        IReadOnlyCollection<long> tagIds,
        CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<string>> ListPathsForTagAsync(
        long tagId,
        CancellationToken cancellationToken = default);
}
