namespace FilesMate.App.Sharing;

public sealed record ShareResult(
    bool InvokedWindowsShare,
    bool UsedPathFallback,
    string? FallbackText);

public interface IShareService
{
    public Task<ShareResult> ShareAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
}
