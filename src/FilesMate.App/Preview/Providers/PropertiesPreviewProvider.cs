namespace FilesMate.App.Preview.Providers;

public sealed class PropertiesPreviewProvider : IPreviewProvider
{
    public bool CanHandle(string path) => true;

    public async Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default) =>
        await PreviewMetadataReader.Shared.ReadAsync(request.Path, cancellationToken).ConfigureAwait(false);
}
