namespace FilesMate.App.Preview.Providers;

public sealed class PdfPreviewProvider : IPreviewProvider
{
    public bool CanHandle(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<PreviewResult> CreateAsync(PreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<PreviewResult>(new PreviewResult.Pdf(request.Path));
    }
}
