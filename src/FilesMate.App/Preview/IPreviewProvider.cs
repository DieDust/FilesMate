namespace FilesMate.App.Preview;

public interface IPreviewProvider
{
    public bool CanHandle(string path);

    public Task<PreviewResult> CreateAsync(
        PreviewRequest request,
        CancellationToken cancellationToken = default);
}
